# Versioning & Release

- Single version source of truth: `<Version>` in `McpPlugin/McpPlugin.csproj`
- `commands/bump-version.ps1` — bumps version across all 3 projects
- `commands/update-reflectornet.ps1` — updates ReflectorNet dependency
- Release = manual dispatch of `release.yml` ONLY (merging to `main` publishes nothing): ref + version guards → tests (3-OS matrix, net8.0 + net9.0) + golden-vector parity + pack rehearsal → NuGet deploy (`deploy.yml`, `workflow_call` only) → wait until all 3 packages are on NuGet → tag + GitHub release. `gh workflow run release.yml --ref main -f dry_run=true` rehearses everything and publishes nothing.
