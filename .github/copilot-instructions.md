# Project Guidelines

## Code Style

- **Namespaces**: Use Java-style reverse domain namespaces (e.g., `com.IvanMurzak.McpPlugin`). This is mandatory and overrides standard C# conventions.
- **File Headers**: ALL C# files MUST start with the specific ASCII art header containing author, repo, and license info. Copy an existing header exactly.
- **Formatting**:
  - Use Allman style (braces on new lines).
  - `_camelCase` for private fields.
  - `PascalCase` for public members.
- **Reactive Programming**: Prefer `R3` (Reactive Extensions) for event handling (`Subject<Unit>`, `Observable<T>`).
- **Data Inspection**: Use `com.IvanMurzak.ReflectorNet` for runtime metadata inspection over standard Reflection where applicable.
- **Logging**: Use `Microsoft.Extensions.Configuration` abstractions backed by **NLog**.

## Architecture

- **Core Library (`McpPlugin`)**: Implements `McpToolManager`, `McpResourceManager`, `McpPromptManager`.
- **Server Integration (`McpPlugin.Server`)**: ASP.NET Core integration.
  - Register: `builder.Services.WithMcpServer(...)`
  - Middleware: `app.UseMcpPluginServer(...)`
- **Common (`McpPlugin.Common`)**: Shared utilities like `IDisposableAsync` and `Version` info.
- **Transports**: Supports `stdio` (local process) and `streamableHttp` (SSE/HTTP).

## Build and Test

- **Build**: `dotnet build McpPlugin.sln`
- **Test**: `dotnet test` (Runs `McpPlugin.Tests` and `McpPlugin.Server.Tests`)
  - Testing uses **xUnit**, **FluentAssertions**, and **Moq**.
- **Automation**: Check `commands/` for PowerShell scripts (e.g., `bump-version.ps1`, `update-reflectornet.ps1`).

## Project Conventions

- **CLI Arguments**: Use `DataArguments` class for parsing command-line arguments and transport settings, not just `IConfiguration`.
- **Disposal**: Implement `IDisposableAsync` for asynchronous cleanup resources.
- **Attributes**: Heavy use of attributes for tool definitions (from `ReflectorNet` or internal MCP attributes).

## Integration Points

- **ASP.NET Core**: See `DemoWebApp` for reference implementation. Use `IDependencyInjection` to resolve services.
- **Console Apps**: See `DemoConsoleApp` for `stdio` transport examples.
- **SignalR**: Used internally in `McpPlugin.Server` for some communication aspects.

## Security

- **Auth**: Primary usage is designed for trusted local environments (stdio) or internal networks.
- **Ports**: Configurable via `DataArguments`, defaults to strict port control.
