<!--
## Sync Impact Report

**Version Change**: 1.0.0 → 1.1.0 (MINOR — new prohibition rule added to Principle IV)

### Modified Principles
- **Principle IV (Test-First Discipline)**: "FluentAssertions" → "Shouldly" in rule 2;
  added rule 7 prohibiting `using FluentAssertions` directives.

### Added Sections
None

### Removed Sections
None

### Template Consistency Status
- `.specify/templates/plan-template.md` ✅ No FluentAssertions references; Constitution Check gate unchanged
- `.specify/templates/spec-template.md` ✅ No testing-library references; compatible
- `.specify/templates/tasks-template.md` ✅ Phase structure unchanged; test-first discipline still applies
- `.specify/templates/commands/` ⚠ Directory does not exist — no command files to validate

### Follow-up TODOs
- `CLAUDE.md` line "xUnit + FluentAssertions + Moq" MUST be updated to "xUnit + Shouldly + Moq"
- `McpPlugin.Server.Tests/*.cs` — 10 files still contain `using FluentAssertions` directives
- `McpPlugin.Server.Tests.csproj` — still references FluentAssertions NuGet package
- `.github/copilot-instructions.md` — references FluentAssertions
-->

# MCP Plugin for .NET Constitution

## Core Principles

### I. Bridge Architecture (NON-NEGOTIABLE)

The system MUST implement the bridge/gateway pattern to decouple .NET applications from MCP
clients at all times.

- .NET applications (Unity, WPF, game servers) MUST connect to `McpPlugin.Server` via SignalR.
- MCP clients (Claude Desktop, MCP Inspector) MUST connect to `McpPlugin.Server` via stdio or HTTP.
- No .NET application MUST ever be spawned directly as a subprocess by an MCP client.
- The hub endpoint MUST be `/hub/mcp-server`. The default bridge port is `8080`.
- The `McpPlugin` client library MUST include automatic reconnection so applications survive
  bridge restarts without manual intervention.

**Rationale**: Heavy .NET applications (Unity, WPF) have independent lifecycles, heavy startup
costs, and require live-context access. Direct subprocess coupling is architecturally incompatible
with these constraints, and the bridge pattern solves all three problems cleanly.

### II. Package-First Design

Every significant capability MUST be delivered as a self-contained, independently buildable and
testable NuGet package. The three canonical packages are:

- `McpPlugin` (netstandard2.1 / net8.0 / net9.0) — client library embedded in .NET apps.
- `McpPlugin.Server` (net8.0 / net9.0) — bridge/gateway hosted via ASP.NET Core.
- `McpPlugin.Common` (netstandard2.1) — shared DTOs, interfaces, and protocol definitions.

Rules:

- Each package MUST be independently buildable and testable.
- A new NuGet package MUST NOT be introduced unless the boundary is architecturally justified
  and documented in the Complexity Tracking table of the relevant plan.md.
- `McpPlugin.Common` MUST remain free of ASP.NET Core and SignalR runtime dependencies.

**Rationale**: NuGet distribution requires clean separation of concerns. The netstandard2.1
target (required for Unity compatibility) constrains which APIs are available in the client and
common packages.

### III. Attribute-Based Registration

Tools, prompts, and resources MUST be registered declaratively via attributes, not via imperative
registration calls.

- Tools: `[McpPluginTool]` on methods inside `[McpPluginToolType]`-annotated classes.
- Prompts: `[McpPluginPrompt]` on methods inside `[McpPluginPromptType]`-annotated classes.
- Resources: `[McpPluginResource]` on methods inside `[McpPluginResourceType]`-annotated classes.
- Reflection MUST use `com.IvanMurzak.ReflectorNet`, not raw `System.Reflection`.
- Assembly scanning MUST be available via `WithToolsFromAssembly()`, `WithPromptsFromAssembly()`,
  and `WithResourcesFromAssembly()` builder methods.

**Rationale**: Attribute-based registration enables zero-boilerplate integration for consumers,
supports fuzzy name matching via ReflectorNet, and enables automatic JSON schema generation so
LLMs understand parameter types precisely.

### IV. Test-First Discipline (NON-NEGOTIABLE)

All new functionality MUST follow Red-Green-Refactor:

1. Write tests → confirm they FAIL → implement → confirm they PASS → refactor.
2. Tests MUST use xUnit + Shouldly + Moq.
3. Tests requiring test isolation MUST use `[Collection("McpPlugin")]`.
4. All tests MUST pass on both `net8.0` and `net9.0` before merging to `main`.
5. `async` tests MUST use `await using` for disposable resources to prevent
   `AsyncTestSyncContext` deadlocks under xUnit.
6. `SubscribeAwait` from R3 MUST NOT be used in production code exercised by tests, as it
   captures the SynchronizationContext and can cause xUnit's test runner to hang indefinitely.
7. `using FluentAssertions` directives MUST NOT appear in any source file. FluentAssertions
   is a prohibited dependency — all assertion needs MUST be fulfilled by Shouldly.

**Rationale**: The bridge architecture spans process boundaries; regressions are expensive to
diagnose. The known xUnit `AsyncTestSyncContext` deadlock risk is prevented by strict disposal
discipline and avoiding SynchronizationContext-capturing reactive operators. FluentAssertions
was replaced by Shouldly for assertion readability and consistency; retaining both libraries
creates confusion and split conventions.

### V. Code Style & Reactive Patterns

All `.cs` files MUST comply with the following non-negotiable rules:

- **Namespace**: Java-style reverse domain — `com.IvanMurzak.McpPlugin.*`.
- **File header**: ASCII art Apache-2.0 license block (copy from any existing `.cs` file).
- **Braces**: Allman style — opening brace on its own line.
- **Fields**: `_camelCase` for private fields; `PascalCase` for public members.
- **Language version**: LangVersion 9.0 for client/server projects; 11.0 for common.
  `ImplicitUsings` MUST be disabled. `Nullable` MUST be enabled.
- **Events/Observables**: MUST use R3 (`Subject<T>`, `Observable<T>`, `ReactiveProperty<T>`).
  `System.Reactive` or the `event` keyword MUST NOT replace R3 where R3 is established.
- **Disposal**: MUST use `CompositeDisposable` with `.AddTo(_disposables)`.
  Per-connection subscriptions MUST use `.RegisterTo(cancellationToken)` instead.
- **Logging**: MUST use `Microsoft.Extensions.Logging` abstractions (NLog on server).
- **DI**: MUST use `Microsoft.Extensions.DependencyInjection`.
- **CLI args**: MUST use `DataArguments`, not raw `IConfiguration`.

**Rationale**: Consistency across Unity (netstandard2.1), server (.NET 8/9), and test projects
is essential for long-term maintainability. R3 is Unity-safe — it does not capture
SynchronizationContext by default, unlike `System.Reactive`.

## Technology Stack

### Runtime Targets

| Package | Target Frameworks |
| --- | --- |
| `McpPlugin` (client) | `netstandard2.1`, `net8.0`, `net9.0` |
| `McpPlugin.Server` (bridge) | `net8.0`, `net9.0` |
| `McpPlugin.Common` (shared) | `netstandard2.1` |

### Required Dependencies

| Dependency | Purpose | Scope |
| --- | --- | --- |
| `Microsoft.AspNetCore.SignalR.Client` | Client-bridge transport | McpPlugin |
| `Microsoft.AspNetCore.SignalR` | Hub hosting | McpPlugin.Server |
| `com.IvanMurzak.ReflectorNet` | Reflection, fuzzy matching, schema gen | McpPlugin, McpPlugin.Server |
| `R3` | Reactive observables and properties | All packages |
| `Microsoft.Extensions.Logging` | Logging abstractions | All packages |
| `Microsoft.Extensions.DependencyInjection` | DI container | All packages |
| `xUnit` + `Shouldly` + `Moq` | Testing | McpPlugin.Tests |

### Transport Protocol

- MCP clients → bridge: `stdio` (local AI agents) or `HTTP` (remote clients).
- .NET apps → bridge: SignalR WebSockets over HTTP; default port `8080`.

## Release & Versioning Workflow

### Version Management

- **Single source of truth**: `<Version>` in `McpPlugin/McpPlugin.csproj`.
- Version MUST follow Semantic Versioning — MAJOR.MINOR.PATCH:
  - MAJOR: Breaking API or protocol changes.
  - MINOR: New backward-compatible features.
  - PATCH: Bug fixes and internal improvements.
- `commands/bump-version.ps1` MUST be used to propagate version changes across all three
  projects. Manual edits to version fields in individual project files are NOT permitted.

### Release Gate (CI/CD)

Push to `main` triggers the automated pipeline in sequence:

1. Version check — guard against unchanged versions.
2. Test suite on `net8.0` AND `net9.0`.
3. GitHub Release creation.
4. NuGet package deployment.

All four gates MUST pass before a release is considered successful.

### Branching Convention

- Feature work: `feature/<description>` branched from `main`.
- Bug fixes: `fix/<description>` branched from `main`.
- PRs target `main`. Direct commits to `main` are NOT permitted.

## Governance

This constitution supersedes all other development practices and documentation. In case of
conflict between this constitution and any other guidance, the constitution takes precedence.

**Amendment Procedure**:

1. Open a PR that updates `.specify/memory/constitution.md`.
2. Increment `CONSTITUTION_VERSION` following semantic versioning:
   - MAJOR: Backward-incompatible removal or redefinition of a core principle.
   - MINOR: New principle or section, or materially expanded guidance.
   - PATCH: Clarifications, wording fixes, or non-semantic refinements.
3. Update `LAST_AMENDED_DATE` to the current ISO date.
4. Run `/speckit.constitution` to propagate changes to dependent templates.
5. Provide a migration plan in the PR if the amendment affects existing feature specs or tasks.

**Compliance**:

- All PRs MUST be reviewed for constitution compliance before merge.
- The Constitution Check gate in plan.md MUST be completed before Phase 0 research begins and
  re-verified after Phase 1 design.
- Complexity violations (extra packages, deviation from established patterns) MUST be documented
  in the Complexity Tracking table of plan.md.
- Use `CLAUDE.md` as the runtime development guidance file for day-to-day coding instructions.

**Version**: 1.1.0 | **Ratified**: 2026-02-28 | **Last Amended**: 2026-03-01
