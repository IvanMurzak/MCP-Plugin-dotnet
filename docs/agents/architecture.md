# Architecture

MCP Plugin for .NET — a bridge architecture that decouples MCP (Model Context Protocol) servers from .NET applications using SignalR. Allows heavy .NET apps (Unity, WPF, game servers) to expose tools/prompts/resources to AI clients like Claude Desktop without being spawned as subprocesses.

Three NuGet packages, one version (currently in `McpPlugin/McpPlugin.csproj`):

- **McpPlugin** (client library, netstandard2.1/net8.0/net9.0/net10.0) — Embedded in .NET apps. Connects to the bridge via SignalR. Key classes: `McpPlugin`, `McpPluginBuilder`, `McpManager`, `McpToolManager`, `ConnectionManager`.
- **McpPlugin.Server** (bridge/gateway, net8.0/net9.0/net10.0) — ASP.NET Core hosted service. MCP clients (Claude) connect via stdio/HTTP; .NET apps connect via SignalR. Key classes: `McpServerService`, `McpServerHub`, `ToolRouter` (static partial), `PromptRouter` (static partial), `ResourceRouter` (static partial).
- **McpPlugin.Common** (shared, netstandard2.1) — DTOs, hub interfaces, `DataArguments` (CLI arg parsing), `Version` handshake.

net10.0 is built only by a .NET 10+ SDK (a `NETCoreSdkVersion` condition in each multi-targeted csproj and
the demos): chain consumers still build this repo from source with SDK 9, which then builds exactly the
netstandard2.1/net8.0/net9.0 set. CI and release install 10.0.x, and `release.yml`/`deploy.yml` fail a
package that lacks any expected `lib/<tfm>`.

```
.NET App ──SignalR──► McpPlugin.Server ◄──stdio/HTTP── Claude Desktop
                      (bridge on :8080)
```

Hub endpoint: `/hub/mcp-server`
