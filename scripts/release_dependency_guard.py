#!/usr/bin/env python3
"""
Release dependency guard: refuse a release that would ship a dependency users cannot restore.

Between test waves MCP-Plugin-dotnet's `main` may build against an UNRELEASED ReflectorNet
(e.g. `com.IvanMurzak.ReflectorNet 5.5.1-dev.ec3702e`) restored from the committed
`prerelease-packages/` feed that `Directory.Build.props` adds to every restore. That is fine for CI,
but a release cut in that state publishes NuGet packages whose dependencies nuget.org cannot
satisfy. This guard runs as the FIRST step of `release.yml` and fails the run (dry runs included)
before anything is built or published while any such pin or feed is left.

What it checks (everything the release publishes: the three NuGet packages packed from
`McpPlugin.Common`, `McpPlugin` and `McpPlugin.Server`; the GitHub Release ships no binaries):
  * every NuGet pin of those projects and the `Directory.Build.props` / `Directory.Packages.props` /
    `Directory.Build.targets` above them plus the `.props` / `.targets` / `.nuspec` files in their
    folders, MSBuild `$(Property)` versions resolved: a pre-release version (any `-` suffix), a
    floating version or a version not listed on nuget.org fails;
  * a local (non-URL) NuGet restore source: `RestoreSources` / `RestoreAdditionalProjectSources` /
    `RestoreFallbackFolders` in those files, or a `nuget.config` package source / fallback folder above
    the projects;
  * a packable project the guard does not know about (the guard is then out of date).
nuget.org being unreachable fails the run too (fail closed). Test, demo and harness projects ship
nothing and are not scanned.

Usage:
    python scripts/release_dependency_guard.py [--root <repo>]
Exit 0 = every pin is a published release; exit 1 = problems listed (file:line: message).
"""
from __future__ import annotations

import argparse
import http.client
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable, Optional

ROOT = Path(__file__).resolve().parents[1]
NUGET_INDEX_URL = "https://api.nuget.org/v3-flatcontainer/{id}/index.json"

# The projects release.yml / deploy.yml pack and push to nuget.org.
PACKED_PROJECTS = (
    "McpPlugin.Common/McpPlugin.Common.csproj",
    "McpPlugin/McpPlugin.csproj",
    "McpPlugin.Server/McpPlugin.Server.csproj",
)
# MSBuild item types that pin a NuGet package version.
PACKAGE_ITEMS = {"PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload"}
# MSBuild properties that add NuGet restore sources.
RESTORE_SOURCE_PROPERTIES = {"RestoreSources", "RestoreAdditionalProjectSources", "RestoreFallbackFolders"}
SKIP_DIRS = {"bin", "obj", "node_modules", "prerelease-packages", "publish", "packages", "TestResults", "nupkg"}
PROPERTY_RE = re.compile(r"\$\(([A-Za-z_][A-Za-z0-9_.-]*)\)")


@dataclass(frozen=True)
class Pin:
    """One dependency version as written in a shipped file."""

    package: str
    version: str
    file: Path
    line: int


@dataclass(frozen=True)
class Problem:
    file: Path
    line: int
    message: str


class RegistryError(Exception):
    """nuget.org could not be queried (network / HTTP error other than 404)."""


class NuGetRegistry:
    """Listed versions per package id from the nuget.org flat container (cached; 404 = unknown package)."""

    def __init__(self, fetch: Optional[Callable[[str], Optional[bytes]]] = None) -> None:
        self._fetch = fetch or _http_get
        self._cache: dict[str, Optional[set[str]]] = {}
        self._outage: Optional[RegistryError] = None

    def versions(self, package: str) -> Optional[set[str]]:
        if self._outage:  # one retry cycle in total: nuget.org down fails the rest at once
            raise self._outage
        key = package.lower()
        if key not in self._cache:
            url = NUGET_INDEX_URL.format(id=key)
            try:
                body = self._fetch(url)
                listed = None if body is None else {
                    normalize_nuget_version(v) for v in json.loads(body.decode("utf-8"))["versions"]
                }
            except (ValueError, KeyError, TypeError, AttributeError) as error:  # not the flat-container JSON
                self._outage = RegistryError(f"{url}: unexpected response ({error!r})")
                raise self._outage from error
            except RegistryError as error:
                self._outage = error
                raise
            self._cache[key] = listed
        return self._cache[key]


def _http_get(url: str) -> Optional[bytes]:
    """GET with retries; None on 404; RegistryError when nuget.org stays unreachable."""
    last: Optional[Exception] = None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            last = error
        except (urllib.error.URLError, TimeoutError, OSError, http.client.HTTPException) as error:
            last = error
        if attempt < 2:
            time.sleep(2 ** attempt)
    raise RegistryError(f"{url}: {last}")


# --- versions ---------------------------------------------------------------------------------


def is_prerelease(version: str) -> bool:
    """SemVer/NuGet pre-release: any `-` suffix on the version (build metadata `+x` alone is not)."""
    return "-" in version.split("+", 1)[0]


def normalize_nuget_version(version: str) -> str:
    """NuGet's normalized form: `1.0` -> `1.0.0`, `1.0.0.0` -> `1.0.0`, leading zeros dropped, metadata dropped."""
    core, _, prerelease = version.split("+", 1)[0].strip().partition("-")
    parts = [str(int(p)) if p.isdigit() else p for p in core.split(".")]
    while len(parts) < 3:
        parts.append("0")
    if len(parts) == 4 and parts[3] == "0":
        parts.pop()
    normalized = ".".join(parts)
    return (normalized + "-" + prerelease if prerelease else normalized).lower()


def nuget_versions_in(spec: str) -> tuple[list[str], Optional[str]]:
    """The concrete versions named by a NuGet version spec (`1.2.3`, `[1.2.3]`, `[1.0,2.0)`), or an error."""
    spec = spec.strip()
    if not spec:
        return [], "empty version"
    if "*" in spec:
        return [], f"floating version '{spec}' is not a pin"
    if spec[0] in "[(":
        if spec[-1] not in "])":
            return [], f"malformed version range '{spec}'"
        lower, _, upper = (b.strip() for b in spec[1:-1].partition(","))
        if spec[0] == "(" and lower:
            return [], f"version range '{spec}' with an exclusive lower bound is not a pin"
        if not lower:
            return [], f"version range '{spec}' without a lower bound is not a pin"
        if "," not in spec:  # `[1.2.3]`: exactly that version
            return [lower], None
        # The lower bound is what restore picks; an exclusive upper bound need not exist (yet).
        return [b for b in (lower, upper if spec[-1] == "]" else "") if b], None
    return [spec], None


# --- MSBuild ----------------------------------------------------------------------------------


def _local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def line_of(text: str, *needles: str) -> int:
    """1-based line of the first line containing every needle (0 when none does)."""
    for number, line in enumerate(text.splitlines(), 1):
        if all(n in line for n in needles):
            return number
    return 0


def rel(path: Path, root: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def ancestors(start: Path, root: Path) -> list[Path]:
    """`start` and its parents up to and including `root`, outermost first."""
    root = root.resolve()
    chain = []
    current = start.resolve()
    while True:
        chain.append(current)
        if current == root or current.parent == current:
            break
        current = current.parent
    return list(reversed(chain))


def walk(directory: Path) -> Iterable[Path]:
    """Files of a project's folder, not descending into build output or another project's folder."""
    for entry in sorted(directory.iterdir()):
        if entry.is_dir():
            if entry.name not in SKIP_DIRS and not entry.name.startswith(".") and not any(entry.glob("*.csproj")):
                yield from walk(entry)
        else:
            yield entry


def msbuild_chain(project: Path, root: Path) -> list[Path]:
    """The `Directory.*.props/targets` above a project (outermost first), the project, and the
    `.props`/`.targets`/`.nuspec` files in its folder."""
    files: list[Path] = []
    for directory in ancestors(project.parent, root):
        for name in ("Directory.Build.props", "Directory.Packages.props", "Directory.Build.targets"):
            if (directory / name).is_file():
                files.append(directory / name)
    files.append(project)
    for path in walk(project.parent):
        if path.suffix in (".props", ".targets", ".nuspec") and path not in files:
            files.append(path)
    return files


def read_properties(files: list[Path]) -> dict[str, list[str]]:
    """Every static `<PropertyGroup>` definition over the chain (Conditions are not evaluated, so a
    property set more than once keeps every value it may take)."""
    properties: dict[str, list[str]] = {}
    for path in files:
        try:
            tree = ET.parse(path)
        except ET.ParseError:
            continue  # reported by scan_msbuild
        for group in tree.getroot().iter():
            if _local(group.tag) != "PropertyGroup":
                continue
            for prop in group:
                if isinstance(prop.tag, str) and prop.text is not None:
                    values = properties.setdefault(_local(prop.tag), [])
                    if prop.text.strip() not in values:
                        values.append(prop.text.strip())
    return properties


def resolve(value: str, properties: dict[str, list[str]], file: Path) -> list[str]:
    """Every expansion of the `$(Name)`s in `value` over the static definitions; an unknown name stays
    as `$(Name)` (the caller reports it)."""
    builtins = {"MSBuildThisFileDirectory": str(file.parent) + os.sep, "MSBuildProjectDirectory": str(file.parent)}
    results: set[str] = set()
    pending = [(value, 0)]
    while pending:
        current, depth = pending.pop()
        match = PROPERTY_RE.search(current)
        name = match.group(1) if match else ""
        options = [builtins[name]] if name in builtins else properties.get(name)
        if not match or not options or depth >= 16:
            results.add(current)
            continue
        pending += [(current[:match.start()] + option + current[match.end():], depth + 1) for option in options]
    return sorted(results)


def is_url(source: str) -> bool:
    return re.match(r"^[a-z][a-z0-9+.-]*://", source, re.IGNORECASE) is not None and not source.lower().startswith("file:")


def item_version(element: ET.Element) -> Optional[str]:
    """The version an item pins; `VersionOverride` wins over `Version` (central package management)."""
    for name in ("VersionOverride", "Version", "version"):
        if element.get(name) is not None:
            return element.get(name)
    for name in ("VersionOverride", "Version"):
        child = next((c for c in element if isinstance(c.tag, str) and _local(c.tag) == name), None)
        if child is not None and child.text:
            return child.text.strip()
    return None


def scan_msbuild(project: Path, root: Path) -> tuple[list[Pin], list[Problem]]:
    pins: list[Pin] = []
    problems: list[Problem] = []
    chain = msbuild_chain(project.resolve(), root)
    properties = read_properties(chain)
    for path in chain:
        text = path.read_text(encoding="utf-8-sig")
        try:
            document = ET.fromstring(text)
        except ET.ParseError as error:
            problems.append(Problem(path, 1, f"cannot parse MSBuild/nuspec XML: {error}"))
            continue
        for element in document.iter():
            if not isinstance(element.tag, str):
                continue
            name = _local(element.tag)
            if name in PACKAGE_ITEMS or (name == "dependency" and path.suffix == ".nuspec"):
                package = element.get("Include") or element.get("Update") or element.get("id")
                raw = item_version(element)
                if not package or raw is None:
                    continue  # a version-less reference takes its pin from a PackageVersion scanned here too
                line = line_of(text, f'"{package}"', raw) or line_of(text, package, raw)
                for version in resolve(raw, properties, path):
                    if PROPERTY_RE.search(version):
                        problems.append(Problem(path, line, f"{package}: cannot resolve version '{raw}'"))
                        continue
                    versions, error = nuget_versions_in(version)
                    if error:
                        problems.append(Problem(path, line, f"{package}: {error}"))
                    pins.extend(Pin(package, v, path, line) for v in versions)
            elif name in RESTORE_SOURCE_PROPERTIES and element.text:
                for entry in element.text.split(";"):
                    entry = entry.strip()
                    if not entry or entry == f"$({name})":
                        continue
                    resolved = [s for value in resolve(entry, properties, path) for s in value.split(";") if s.strip()]
                    if not all(is_url(source.strip()) for source in resolved):
                        problems.append(Problem(path, line_of(text, f"<{name}"),
                                                f"{name} adds the local NuGet feed '{entry}' "
                                                f"(a release must restore from nuget.org only; delete the "
                                                f"pre-release feed when the pins return to published versions)"))
    return pins, problems


def scan_nuget_configs(project: Path, root: Path) -> list[Problem]:
    problems: list[Problem] = []
    for directory in ancestors(project.parent, root):
        for entry in directory.iterdir():
            if entry.is_file() and entry.name.lower() == "nuget.config":
                text = entry.read_text(encoding="utf-8-sig")
                try:
                    tree = ET.fromstring(text)
                except ET.ParseError as error:
                    problems.append(Problem(entry, 1, f"cannot parse nuget.config: {error}"))
                    continue
                for section, label in (("packageSources", "package source"), ("fallbackPackageFolders", "fallback folder")):
                    for sources in tree.iter(section):
                        for add in sources.iter("add"):
                            value = add.get("value", "")
                            if value and not is_url(value):
                                problems.append(Problem(entry, line_of(text, value),
                                                        f"{label} '{add.get('key')}' is the local feed '{value}'"))
    return problems


def check_pins(pins: list[Pin], registry: NuGetRegistry) -> list[Problem]:
    problems: list[Problem] = []
    for pin in pins:
        if is_prerelease(pin.version):
            problems.append(Problem(pin.file, pin.line, f"{pin.package} {pin.version} is a pre-release version"))
            continue
        try:
            listed = registry.versions(pin.package)
        except RegistryError as error:
            problems.append(Problem(pin.file, pin.line, f"{pin.package} {pin.version}: cannot verify on nuget.org ({error})"))
            continue
        if listed is None:
            problems.append(Problem(pin.file, pin.line, f"{pin.package} is not a package on nuget.org"))
        elif normalize_nuget_version(pin.version) not in listed:
            problems.append(Problem(pin.file, pin.line, f"{pin.package} {pin.version} is not published on nuget.org"))
    return problems


# --- this repository --------------------------------------------------------------------------


def unknown_packable_projects(root: Path) -> list[Problem]:
    """A csproj marked `<IsPackable>true</IsPackable>` that the guard does not scan: the release may
    publish it, so the guard is out of date."""
    known = {(root / p).resolve() for p in PACKED_PROJECTS}
    problems: list[Problem] = []
    for path in sorted(root.rglob("*.csproj")):
        folders = path.relative_to(root).parts[:-1]
        if path.resolve() in known or any(f in SKIP_DIRS or f.startswith(".") for f in folders):
            continue
        text = path.read_text(encoding="utf-8-sig")
        if re.search(r"<IsPackable>\s*true\s*</IsPackable>", text, re.IGNORECASE):
            problems.append(Problem(path, line_of(text, "<IsPackable>"),
                                    "packable project not scanned (add it to PACKED_PROJECTS in "
                                    "scripts/release_dependency_guard.py)"))
    return problems


def collect(root: Path) -> tuple[list[Pin], list[Problem]]:
    """Every dependency pin and restore source the three published NuGet packages use."""
    problems: list[Problem] = []
    pins: list[Pin] = []
    for relative in PACKED_PROJECTS:
        project = root / relative
        if not project.is_file():
            problems.append(Problem(project, 1, "expected file is missing (update scripts/release_dependency_guard.py)"))
            continue
        project_pins, msbuild_problems = scan_msbuild(project, root)
        pins += project_pins
        problems += msbuild_problems + scan_nuget_configs(project, root)
        if not project_pins:
            problems.append(Problem(project, 1, "no NuGet pins found (update scripts/release_dependency_guard.py)"))
    problems += unknown_packable_projects(root)
    return pins, problems


def run(root: Path, registry: NuGetRegistry) -> tuple[list[Pin], list[Problem]]:
    pins, problems = collect(root)
    problems += check_pins(pins, registry)
    unique = sorted(set(problems), key=lambda p: (rel(p.file, root), p.line, p.message))
    return pins, unique


def main(argv: Optional[list[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.strip().split("\n\n", 1)[0])
    parser.add_argument("--root", type=Path, default=ROOT, help="repository root (default: this checkout)")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    pins, problems = run(root, NuGetRegistry())
    files = sorted({rel(p.file, root) for p in pins})
    print(f"Release dependency guard: {len(pins)} pins in {len(files)} shipped files")
    for name in files:
        print(f"  {name}")
    if not problems:
        print("OK: every dependency pin is a published release and restore uses nuget.org only.")
        return 0
    annotate = os.environ.get("GITHUB_ACTIONS") == "true"
    print(f"\nFAIL: {len(problems)} problem(s); this release would ship dependencies users cannot restore:")
    for problem in problems:
        location, line = rel(problem.file, root), problem.line or 1
        print(f"  {location}:{line}: {problem.message}")
        if annotate:
            print(f"::error file={location},line={line}::{problem.message}")
    print("\nPin a published ReflectorNet (and every other dependency), delete prerelease-packages/ and the "
          "Directory.Build.props feed (plus its .gitignore exception and Dockerfile.local COPY lines) before releasing.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
