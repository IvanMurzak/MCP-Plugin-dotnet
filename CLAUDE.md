# CLAUDE.md

## What this is

MCP Plugin for .NET — a SignalR bridge that lets heavy .NET apps (Unity, WPF, game servers) expose MCP tools/prompts/resources to AI clients like Claude Desktop without being spawned as subprocesses. Three NuGet packages: `McpPlugin` (client), `McpPlugin.Server` (bridge), `McpPlugin.Common` (shared).

## Build / run

```bash
dotnet build McpPlugin.sln
dotnet test
cd DemoWebApp && dotnet run port=11111 client-transport=stdio
```

## Critical invariants

- ALL `.cs` files must start with the ASCII art Apache 2.0 license header — copy from any existing file.
- Namespaces are reverse-domain `com.IvanMurzak.McpPlugin.*` — not standard C# convention.
- Version is single-sourced in `McpPlugin/McpPlugin.csproj`; use `commands/bump-version.ps1` to change it.
- Public API changes here cascade to Unity-MCP and downstream extensions — notify the lead.

## Find detail in

- `docs/claude/architecture.md` — Bridge architecture, packages, SignalR hub endpoint
- `docs/claude/patterns.md` — `[McpPluginToolType]` / `[McpPluginTool]` examples, builder & server registration
- `docs/claude/style.md` — Code style mandates (LangVersion, R3, NLog, DI, disposal)
- `docs/claude/testing.md` — xUnit + Shouldly + Moq conventions, filtered test commands
- `docs/claude/release.md` — Versioning, bump-version, ReflectorNet updates, CI release flow
