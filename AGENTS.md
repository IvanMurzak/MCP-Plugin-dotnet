# AGENTS.md

MCP Plugin for .NET — a SignalR bridge that lets heavy .NET apps (Unity, WPF, game servers) expose
MCP tools/prompts/resources to AI clients without being spawned as subprocesses. Three NuGet
packages: `McpPlugin` (client), `McpPlugin.Server` (bridge), `McpPlugin.Common` (shared).

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
- Public API changes here cascade to Unity-MCP and downstream extensions — call them out to the owner in the PR.

## Layout

- `McpPlugin/`, `McpPlugin.Server/`, `McpPlugin.Common/` — the three packed libraries; source under `src/<area>/`.
- `McpPlugin.Tests/`, `McpPlugin.Server.Tests/` — xUnit tests for the client and the server; put a
  test in the project of the code it covers.
- `McpPlugin.Tests.LockHarness/`, `McpPlugin.Tests.RefreshHarness/` — helper processes used by tests.
- `McpPlugin.NullEngine/` — engine-free test host (see its README).
- `DemoWebApp/`, `DemoConsoleApp/` — runnable demos.
- `commands/` — maintainer PowerShell (version bump, ReflectorNet update, release notes).
- `scripts/` + `tests/chain-fixtures/` — chain-testing fixture tooling (`docs/chain-fixtures.md`).
- `docs/agents/` — agent reference docs (below); other `docs/` — design notes and migration guides.
- `.specify/`, `specs/` — spec-kit constitution, templates and feature specs.

## Find detail in

- `docs/agents/architecture.md` — Bridge architecture, packages, SignalR hub endpoint
- `docs/agents/patterns.md` — `[AiToolType]` / `[AiTool]` examples (and the `[McpPlugin*]` obsolete-alias compatibility note), builder & server registration
- `docs/agents/style.md` — Code style mandates (LangVersion, R3, NLog, DI, disposal)
- `docs/agents/testing.md` — xUnit + Shouldly + Moq conventions, filtered test commands
- `docs/agents/release.md` — Versioning, bump-version, ReflectorNet updates, CI release flow
