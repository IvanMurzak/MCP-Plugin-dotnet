#!/usr/bin/env python3
"""
Generate Markdown release notes for the commits between two refs.

Writes `release_<from>_to_<to>.md` in the current directory: a compare link, then one line per
commit (newest first) with its short SHA, subject and GitHub author login (`gh api`). A commit whose
GitHub author cannot be resolved is skipped with a warning (it gets no line).

Usage:
    python commands/generate_release.py --version-from 8.13.0 --version-to 8.13.1
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path
from typing import List, Optional


def capture(*args: str) -> str:
    """stdout of a command ("" when it fails or is not installed). git's stderr (e.g. `fatal: bad
    revision`) reaches the console; gh's is discarded (its failures are reported per commit)."""
    stderr = subprocess.DEVNULL if args[0] == "gh" else None
    try:
        result = subprocess.run(list(args), stdout=subprocess.PIPE, stderr=stderr,
                                text=True, encoding="utf-8", errors="replace")
    except OSError:
        return ""
    return result.stdout


def repo_url() -> str:
    url = re.sub(r"\.git$", "", capture("git", "remote", "get-url", "origin").strip(), flags=re.IGNORECASE)
    match = re.match(r"^git@github\.com:(.+)", url, re.IGNORECASE)
    return f"https://github.com/{match.group(1)}" if match else url


def github_login(repo_path: str, sha: str) -> Optional[str]:
    data = capture("gh", "api", f"repos/{repo_path}/commits/{sha}",
                   "--jq", ".author.login // .commit.author.name").rstrip("\r\n")
    if data.startswith("{"):
        print(f"GitHub API error for commit {sha}: {data}")
        return None
    return data if data.strip() else None


def generate(version_from: str, version_to: str) -> Path:
    url = repo_url()
    repo_path = re.sub(r"^https://github\.com/", "", url, flags=re.IGNORECASE)
    lines = [
        "## Comparison",
        f"See every change: [Compare {version_from}...{version_to}]({url}/compare/{version_from}...{version_to})",
        "",
        "---",
        "",
        "## Commit Summary (Newest → Oldest)",
    ]
    log = capture("git", "log", "--pretty=format:%H%x09%s", f"{version_from}..{version_to}")
    for sha, _, message in (line.partition("\t") for line in log.splitlines()):
        username = github_login(repo_path, sha)
        if not username:
            print(f"Using git fallback for commit {sha}")
            continue
        lines.append(f"- [`{sha[:6]}`]({url}/commit/{sha}) — {message} by @{username}")

    path = Path(f"release_{version_from}_to_{version_to}.md")
    # Text mode writes the platform newline after every line.
    with path.open("w", encoding="utf-8") as f:
        f.writelines(line + "\n" for line in lines)
    print(f"Release notes generated successfully in {path}")
    return path


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--version-from", required=True, help="older ref (tag), e.g. 8.13.0")
    parser.add_argument("--version-to", required=True, help="newer ref (tag), e.g. 8.13.1")
    args = parser.parse_args(argv)
    generate(args.version_from, args.version_to)
    return 0


if __name__ == "__main__":
    # Commit subjects and gh errors may hold characters a Windows code-page console cannot encode.
    sys.stdout.reconfigure(errors="backslashreplace")
    raise SystemExit(main())
