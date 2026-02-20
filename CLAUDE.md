# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MCP Plugin for .NET — a bridge architecture that decouples MCP (Model Context Protocol) servers from .NET applications using SignalR. Allows heavy .NET apps (Unity, WPF, game servers) to expose tools/prompts/resources to AI clients like Claude Desktop without being spawned as subprocesses.

## Build & Test Commands

```bash
# Build
dotnet build McpPlugin.sln

# Run all tests
dotnet test

# Run a specific test project
dotnet test McpPlugin.Tests/McpPlugin.Tests.csproj

# Run a specific test class or method
dotnet test --filter "ClassName=McpBuilderTests"
dotnet test --filter "FullyQualifiedName~McpBuilderTests.Build_WithoutLogging_ShouldSucceed"

# Run demo server (stdio transport for Claude Desktop)
cd DemoWebApp && dotnet run port=11111 client-transport=stdio
```

## Architecture

Three NuGet packages, one version (currently in `McpPlugin/McpPlugin.csproj`):

- **McpPlugin** (client library, netstandard2.1/net8.0/net9.0) — Embedded in .NET apps. Connects to the bridge via SignalR. Key classes: `McpPlugin`, `McpPluginBuilder`, `McpManager`, `McpToolManager`, `ConnectionManager`.
- **McpPlugin.Server** (bridge/gateway, net8.0/net9.0) — ASP.NET Core hosted service. MCP clients (Claude) connect via stdio/HTTP; .NET apps connect via SignalR. Key classes: `McpServerService`, `McpServerHub`, `ToolRouter`, `PromptRouter`, `ResourceRouter`.
- **McpPlugin.Common** (shared, netstandard2.1) — DTOs, hub interfaces, `DataArguments` (CLI arg parsing), `Version` handshake.

```
.NET App ──SignalR──► McpPlugin.Server ◄──stdio/HTTP── Claude Desktop
                      (bridge on :8080)
```

Hub endpoint: `/hub/mcp-server`

### Server registration pattern
```csharp
builder.Services.WithMcpServer(dataArguments).WithMcpPluginServer(dataArguments);
app.UseMcpPluginServer(dataArguments);
```

### Client builder pattern
```csharp
new McpPluginBuilder(version)
    .WithConfig(config => config.Host = "http://localhost:8080")
    .WithToolsFromAssembly(typeof(MyTools).Assembly)
    .Build(reflector);
```

### Attribute-based component registration
```csharp
[McpPluginToolType]
public static class MyTools
{
    [McpPluginTool("add", "Adds two numbers")]
    public static int Add(int a, int b) => a + b;
}
```

## Code Style (Mandatory)

- **Namespaces**: Java-style reverse domain — `com.IvanMurzak.McpPlugin.*` (not standard C# convention)
- **File headers**: ALL `.cs` files must start with the ASCII art license header. Copy from any existing file.
- **Formatting**: Allman braces, `_camelCase` private fields, `PascalCase` public members
- **Language**: `LangVersion` 9.0 (client/server) or 11.0 (common), `ImplicitUsings` disabled, `Nullable` enabled
- **Reactive**: Use `R3` library (`Subject<T>`, `Observable<T>`) for event handling
- **Reflection**: Use `com.IvanMurzak.ReflectorNet` over `System.Reflection`
- **Logging**: `Microsoft.Extensions.Logging` abstractions (NLog backend on server)
- **DI**: `Microsoft.Extensions.DependencyInjection`
- **Disposal**: Use `CompositeDisposable` pattern with `.AddTo(_disposables)`
- **CLI args**: Use `DataArguments` class, not raw `IConfiguration`

## Testing Conventions

- xUnit + FluentAssertions + Moq
- Arrange-Act-Assert pattern
- `[Fact]` for single scenarios, `[Theory]` for parameterized
- `[Collection("McpPlugin")]` for test isolation
- Custom `TestLoggerFactory` / `XunitTestOutputLoggerProvider` for test logging

## Versioning & Release

- Single version source of truth: `<Version>` in `McpPlugin/McpPlugin.csproj`
- `commands/bump-version.ps1` — bumps version across all 3 projects
- `commands/update-reflectornet.ps1` — updates ReflectorNet dependency
- Push to `main` triggers: version check → test (net8.0 + net9.0) → GitHub release → NuGet deploy
