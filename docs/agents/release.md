# Versioning & Release

- Single version source of truth: `<Version>` in `McpPlugin/McpPlugin.csproj`
- `python commands/bump_version.py --new-version <x.y.z> [--what-if]` — bumps version across all 3 projects
- `python commands/update_reflectornet.py [--what-if]` — updates ReflectorNet dependency
- `python commands/generate_release.py --version-from <tag> --version-to <tag>` — release notes for a range
- The tooling is stdlib-only Python 3.9+; no PowerShell. Tests: `python -m unittest discover -s scripts -p "test_*.py"`
- Release = manual dispatch of `release.yml` ONLY (merging to `main` publishes nothing): ref + version guards → tests (3-OS matrix, net8.0 + net9.0) + golden-vector parity + pack rehearsal → NuGet deploy (`deploy.yml`, `workflow_call` only) → wait until all 3 packages are on NuGet → tag + GitHub release. `gh workflow run release.yml --ref main -f dry_run=true` rehearses everything and publishes nothing.
