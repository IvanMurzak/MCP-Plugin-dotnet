"""Cases for the Python maintainer tooling (hermetic: temp copies, mocked dotnet/git/gh).

Run: python -m unittest discover -s scripts -p "test_*.py"
"""
from __future__ import annotations

import contextlib
import io
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "commands"))
sys.path.insert(0, str(ROOT / "scripts"))
import bump_version  # noqa: E402
import generate_release  # noqa: E402
import phase2_gate_matrix  # noqa: E402
import update_reflectornet  # noqa: E402

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>8.13.1</Version>
    <Copyright>Copyright © 2026 Ivan Murzak</Copyright>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="com.IvanMurzak.ReflectorNet" Version="5.5.0" />
    <PackageReference Include="Some.Package" Version="8.13.1" />
  </ItemGroup>
</Project>
"""
SERVER_JSON = """{
  "name": "io.github.IvanMurzak/mcp-plugin",
  "version": "8.13.1",
  "packages": [
    {
      "identifier": "com.IvanMurzak.McpPlugin.Server",
      "version": "8.13.1"
    }
  ]
}
"""
CONSTS_CS = """namespace com.IvanMurzak.McpPlugin.Common
{
    public static class Consts
    {
        public const string PluginVersion = "8.13.1";
    }
}
"""
FILES = {
    "McpPlugin.Server/server.json": SERVER_JSON,
    "McpPlugin.Common/McpPlugin.Common.csproj": CSPROJ,
    "McpPlugin.Server/McpPlugin.Server.csproj": CSPROJ,
    "McpPlugin/McpPlugin.csproj": CSPROJ,
    "McpPlugin.Common/src/Utils/Consts.cs": CONSTS_CS,
}


def run_quiet(fn, *args):
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = fn(*args)
    return code, out.getvalue()


def completed(code=0, stdout=""):
    return subprocess.CompletedProcess([], code, stdout=stdout)


class TempTree(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.write_tree("\n")

    def tearDown(self):
        self.tmp.cleanup()

    def write_tree(self, newline, bom=b""):
        for rel, text in FILES.items():
            path = self.root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(bom + text.replace("\n", newline).encode("utf-8"))

    def read(self, rel):
        return (self.root / rel).read_bytes().decode("utf-8")


class BumpVersionTests(TempTree):
    def bump(self, *args):
        return run_quiet(bump_version.main, ["--root", str(self.root), *args])

    def expected(self, newline="\n"):
        csproj = CSPROJ.replace("<Version>8.13.1</Version>", "<Version>8.14.0</Version>")
        expected = {
            "McpPlugin.Server/server.json": SERVER_JSON.replace('"version": "8.13.1"', '"version": "8.14.0"'),
            "McpPlugin.Common/McpPlugin.Common.csproj": csproj,
            "McpPlugin.Server/McpPlugin.Server.csproj": csproj,
            "McpPlugin/McpPlugin.csproj": csproj,
            "McpPlugin.Common/src/Utils/Consts.cs": CONSTS_CS.replace('"8.13.1"', '"8.14.0"'),
        }
        return {rel: text.replace("\n", newline) for rel, text in expected.items()}

    def test_bump_rewrites_only_the_version_strings(self):
        code, out = self.bump("--new-version", "8.14.0")
        self.assertEqual(code, 0, out)
        for rel, text in self.expected().items():
            self.assertEqual(self.read(rel), text, rel)
        # A dependency that happens to share the version is left alone.
        self.assertIn('Include="Some.Package" Version="8.13.1"', self.read("McpPlugin/McpPlugin.csproj"))
        self.assertIn("Total replacements: 6", out)

    def test_crlf_is_preserved_and_bom_dropped(self):
        self.write_tree("\r\n", bom=b"\xef\xbb\xbf")
        code, out = self.bump("--new-version", "8.14.0")
        self.assertEqual(code, 0, out)
        for rel, text in self.expected("\r\n").items():
            self.assertEqual((self.root / rel).read_bytes(), text.encode("utf-8"), rel)

    def test_what_if_writes_nothing(self):
        code, out = self.bump("--new-version", "8.14.0", "--what-if")
        self.assertEqual(code, 0, out)
        self.assertIn("Files to be modified: 5", out)
        for rel, text in FILES.items():
            self.assertEqual(self.read(rel), text, rel)

    def test_same_version_is_a_no_op(self):
        code, out = self.bump("--new-version", "8.13.1")
        self.assertEqual(code, 0, out)
        self.assertIn("same as current version", out)

    def test_invalid_version_fails(self):
        code, _ = self.bump("--new-version", "8.14")
        self.assertEqual(code, 1)

    def test_missing_version_source_fails(self):
        (self.root / "McpPlugin/McpPlugin.csproj").unlink()
        code, out = self.bump("--new-version", "8.14.0")
        self.assertEqual(code, 1)
        self.assertIn("Could not find McpPlugin.csproj", out)

    def test_no_matches_anywhere_fails(self):
        for rel in FILES:
            if rel != "McpPlugin/McpPlugin.csproj":
                (self.root / rel).unlink()
        (self.root / "McpPlugin/McpPlugin.csproj").write_text("<Version>8.13.1</Version >", encoding="utf-8")
        code, out = self.bump("--new-version", "8.14.0")
        self.assertEqual(code, 1, out)


class UpdateReflectorNetTests(TempTree):
    def test_what_if_runs_no_dotnet(self):
        with mock.patch.object(update_reflectornet.subprocess, "run") as run:
            code, out = run_quiet(update_reflectornet.main, ["--root", str(self.root), "--what-if"])
        self.assertEqual(code, 0, out)
        run.assert_not_called()
        self.assertIn("McpPlugin.csproj : 5.5.0", out)

    def test_updates_every_project(self):
        calls = []

        def fake_run(args, **_kwargs):
            calls.append(args[1:])
            if args[1] == "add":
                path = Path(args[2])
                path.write_text(path.read_text(encoding="utf-8").replace('Version="5.5.0"', 'Version="5.6.0"'),
                                encoding="utf-8")
                return completed(1 if path.name == "McpPlugin.Server.csproj" else 0, "boom")
            return completed()

        with mock.patch.object(update_reflectornet.subprocess, "run", side_effect=fake_run):
            code, out = run_quiet(update_reflectornet.main, ["--root", str(self.root)])
        self.assertEqual(code, 0, out)
        self.assertEqual(calls[0], ["nuget", "locals", "all", "--clear"])
        self.assertEqual([c[1] for c in calls[1:]], [str(self.root / p) for p in update_reflectornet.PROJECT_FILES])
        self.assertTrue(all(c[2:] == ["package", "com.IvanMurzak.ReflectorNet"] for c in calls[1:]))
        self.assertIn("Updated to 5.6.0", out)
        self.assertIn("Failed to update: boom", out)


class GenerateReleaseTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.cwd = os.getcwd()
        os.chdir(self.tmp.name)

    def tearDown(self):
        os.chdir(self.cwd)
        self.tmp.cleanup()

    def test_writes_compare_link_and_commit_lines(self):
        outputs = {
            ("git", "remote", "get-url", "origin"): "git@github.com:IvanMurzak/MCP-Plugin-dotnet.git\n",
            ("git", "log", "--pretty=format:%H%x09%s", "1.0.0..1.1.0"): "aaaaaaaaaa\tfeat: thing\nbbbbbbbbbb\tfix: other",
            ("gh", "api", "repos/IvanMurzak/MCP-Plugin-dotnet/commits/aaaaaaaaaa",
             "--jq", ".author.login // .commit.author.name"): "octo\n",
            ("gh", "api", "repos/IvanMurzak/MCP-Plugin-dotnet/commits/bbbbbbbbbb",
             "--jq", ".author.login // .commit.author.name"): "",
        }
        with mock.patch.object(generate_release, "capture", side_effect=lambda *a: outputs.get(a, "")):
            _, out = run_quiet(generate_release.main, ["--version-from", "1.0.0", "--version-to", "1.1.0"])
        url = "https://github.com/IvanMurzak/MCP-Plugin-dotnet"
        text = Path("release_1.0.0_to_1.1.0.md").read_text(encoding="utf-8")
        self.assertEqual(text, "\n".join([
            "## Comparison",
            f"See every change: [Compare 1.0.0...1.1.0]({url}/compare/1.0.0...1.1.0)",
            "",
            "---",
            "",
            "## Commit Summary (Newest → Oldest)",
            f"- [`aaaaaa`]({url}/commit/aaaaaaaaaa) — feat: thing by @octo",
        ]) + "\n")
        # A commit whose GitHub author is unresolved gets no line.
        self.assertIn("Using git fallback for commit bbbbbbbbbb", out)


class Phase2GateTests(unittest.TestCase):
    def gate(self, *args, codes=None):
        calls = []

        def fake_run(args_, **_kwargs):
            calls.append(args_[1:])
            return completed((codes or {}).get(args_[1], 0))

        with mock.patch.object(phase2_gate_matrix.subprocess, "run", side_effect=fake_run):
            code, out = run_quiet(phase2_gate_matrix.main, list(args))
        return code, out, calls

    def test_full_gate_builds_then_runs_both_suites(self):
        code, out, calls = self.gate()
        self.assertEqual(code, 0, out)
        self.assertEqual([c[0] for c in calls], ["restore", "build", "test", "test"])
        self.assertIn("FullyQualifiedName~IsolationMatrix", calls[2])
        self.assertIn("FullyQualifiedName~ProjectIdentityGoldenVector", calls[3])
        self.assertEqual(calls[3][-2:], ["--", "RunConfiguration.TreatNoTestsAsError=true"])
        self.assertIn("Phase-2 gate: PASS", out)

    def test_no_build_skips_restore_and_build(self):
        _, _, calls = self.gate("--no-build")
        self.assertEqual([c[0] for c in calls], ["test", "test"])

    def test_failure_stops_the_gate_with_its_exit_code(self):
        code, out, calls = self.gate(codes={"build": 3})
        self.assertEqual(code, 3)
        self.assertEqual([c[0] for c in calls], ["restore", "build"])
        self.assertIn("build failed", out)
        self.assertNotIn("PASS", out)


if __name__ == "__main__":
    unittest.main()
