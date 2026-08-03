# Testing Conventions

- xUnit + Shouldly + Moq
- Arrange-Act-Assert pattern
- `[Fact]` for single scenarios, `[Theory]` for parameterized
- Collection names are **project-scoped**: `[Collection("McpPlugin")]` in `McpPlugin.Tests`,
  `[Collection("McpPlugin.Server")]` in `McpPlugin.Server.Tests`. There is no repo-wide name — match
  the project you are editing. `[Collection]` is also the ONLY serialization mechanism here (no
  `CollectionDefinition`, no `[assembly: CollectionBehavior]`, no `xunit.runner.json`), and not every
  class carries it, so classes without it DO run in parallel with yours.

## Logging in tests

The two test projects log through **different** stacks, and a helper from one is useless in the
other. Pick by project:

### `McpPlugin.Tests` — Microsoft.Extensions.Logging (MEL)

- `TestLoggerFactory` / `XunitTestOutputLoggerProvider` (`McpPlugin.Tests/Infrastructure/`) are MEL
  providers, and they live in `McpPlugin.Tests`. Use them **there** instead of `NullLogger`, so a
  failure emits to the test output console.
- They are **not available in `McpPlugin.Server.Tests`** — different project, no reference — and
  reaching for them there fails twice over: the type is unreachable, and even if it were reachable a
  MEL provider cannot observe what the server-side routers write (next section).

### `McpPlugin.Server.Tests` — NLog, via `CapturedRouterLogs`

The server-side routers (`PromptRouter`, `ResourceRouter`, `ToolRouter`) log through NLog's **static
`LogManager`**, which is independent of the host's MEL pipeline — `builder.Logging.ClearProviders()`
does not silence them, and no MEL provider ever sees them. A test wired to a MEL logger therefore
asserts on nothing and stays green even when the log line under test is deleted. That is not
hypothetical: during PR #198 deleting a router's `Warn` line left all 676 server tests passing.

To assert on what a router told the operator, capture on the **NLog** side with the shared helper
`McpPlugin.Server.Tests/Infrastructure/CapturedRouterLogs.cs`:

```csharp
using var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));

// ... drive the degraded path over a real host ...

logs.Text.ShouldContain(expectedFailureText, Case.Sensitive);
```

- Takes a `Type` rather than a generic parameter because the routers are **static** classes, which
  C# forbids as type arguments. The rule is scoped to `routerType.FullName`, which is exactly the
  logger name `LogManager.GetCurrentClassLogger()` produces, so renaming a router cannot silently
  stop the capture matching.
- Captures **Warn and above** by default (the operator-visible band); pass an explicit
  `NLog.LogLevel` for a lower floor.
- `LogManager.Configuration` is process-global, so the helper serializes every capture behind an
  assembly-wide gate and restores the exact previous configuration on `Dispose` — always use
  `using var`, and prefer `[Collection("McpPlugin.Server")]` on the calling class so log-asserting
  tests do not queue on that gate.
- Its own guarantees (scoping, level floor, restore, mutual exclusion) are pinned by
  `McpPlugin.Server.Tests/RouterLogCaptureTests.cs`.
- Assert **presence** of the expected text rather than the absence of something else: an absence
  assertion is satisfied for free by a capture that recorded nothing at all, which is the exact
  failure mode this helper exists to rule out.

## Test commands

```bash
# Run all tests
dotnet test

# Run a specific test project
dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj

# Run a specific test class or method
dotnet test --filter "FullyQualifiedName~McpBuilderTests"
dotnet test --filter "FullyQualifiedName~McpBuilderTests.Build_WithoutLogging_ShouldSucceed"
```

- `FullyQualifiedName~` (substring match) is the filter to use for a class OR a method. **Do not use
  `ClassName=`** — it is an MSTest property that the xUnit adapter ignores, so it matches nothing,
  and `dotnet test` exits **0** on a zero-match filter. A filtered run is only green if its summary
  line shows a non-zero `Total:`.
