#!/usr/bin/env python3
"""
Update the com.IvanMurzak.ReflectorNet package to the latest NuGet version.

Clears the local NuGet caches, then runs `dotnet add <project> package com.IvanMurzak.ReflectorNet`
for each of the three package projects so each picks up the latest published version. `--what-if`
lists the current versions and the projects that would be updated, without touching anything.

Usage:
    python commands/update_reflectornet.py
    python commands/update_reflectornet.py --what-if
Exit 0 = done (a project that fails to update is reported, not fatal); exit 1 = an error.
"""
from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path
from typing import List, Optional

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_NAME = "com.IvanMurzak.ReflectorNet"
PROJECT_FILES = (
    "McpPlugin.Common/McpPlugin.Common.csproj",
    "McpPlugin/McpPlugin.csproj",
    "McpPlugin.Server/McpPlugin.Server.csproj",
)
PACKAGE_VERSION_RE = re.compile(
    r'PackageReference Include="' + re.escape(PACKAGE_NAME) + r'" Version="([^"]+)"', re.IGNORECASE)


def current_package_version(project: Path) -> Optional[str]:
    if not project.is_file():
        return None
    match = PACKAGE_VERSION_RE.search(project.read_text(encoding="utf-8-sig"))
    return match.group(1) if match else None


def dotnet(*args: str) -> subprocess.CompletedProcess:
    # Output is captured (the .ps1 did `2>&1` into a variable) and shown only on failure.
    return subprocess.run(["dotnet", *args], stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding="utf-8", errors="replace")


def run(root: Path, what_if: bool) -> int:
    print("ReflectorNet Package Update Script")
    print("==================================")
    print(f"Package: {PACKAGE_NAME}\n")

    print("Current versions:")
    for project in PROJECT_FILES:
        version = current_package_version(root / project)
        print(f"   {Path(project).name} : {version or 'not found'}")

    if what_if:
        print("\nPreview mode - would update the following projects:")
        for project in PROJECT_FILES:
            if (root / project).is_file():
                print(f"   {project}")
        print("\nPreview completed. Run without --what-if to apply changes.")
        return 0

    print("\nClearing local NuGet cache...")
    result = dotnet("nuget", "locals", "all", "--clear")
    if result.returncode == 0:
        print("   Cache cleared successfully")
    else:
        print(f"   [warn] Cache clear warning: {result.stdout.strip()}")

    print("\nUpdating to latest version...")
    for project in PROJECT_FILES:
        path = root / project
        if not path.is_file():
            print(f"[warn] Project not found: {project}")
            continue
        print(f"Updating {path.name}...")
        result = dotnet("add", str(path), "package", PACKAGE_NAME)
        if result.returncode == 0:
            print(f"   Updated to {current_package_version(path)}")
        else:
            print(f"   [fail] Failed to update: {result.stdout.strip()}")

    print("\nReflectorNet update completed!")
    print("Remember to commit these changes to git")
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--what-if", "--dry-run", dest="what_if", action="store_true",
                        help="preview which projects would be updated without applying changes")
    parser.add_argument("--root", type=Path, default=ROOT, help="repo root to edit (default: this checkout)")
    args = parser.parse_args(argv)
    try:
        return run(args.root, args.what_if)
    except Exception as exc:  # mirror the .ps1 catch-all: report and exit 1
        print(f"\n[fail] Script failed: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
