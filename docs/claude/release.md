# Versioning & Release

- Single version source of truth: `<Version>` in `McpPlugin/McpPlugin.csproj`
- `commands/bump-version.ps1` — bumps version across all 3 projects
- `commands/update-reflectornet.ps1` — updates ReflectorNet dependency
- Push to `main` triggers: version check → test (net8.0 + net9.0) → GitHub release → NuGet deploy
