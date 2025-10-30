# MCP Plugin (dotnet)

[![NuGet](https://img.shields.io/nuget/v/com.IvanMurzak.McpPlugin?label=NuGet&labelColor=333A41)](https://www.nuget.org/packages/com.IvanMurzak.McpPlugin/)
[![netstandard2.1](https://img.shields.io/badge/.NET-netstandard2.1-blue?logoColor=white&labelColor=333A41)](https://github.com/IvanMurzak/MCP-Plugin-dotnet)
[![Tests](https://github.com/IvanMurzak/MCP-Plugin-dotnet/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/IvanMurzak/MCP-Plugin-dotnet/actions/workflows/release.yml)

[![Stars](https://img.shields.io/github/stars/IvanMurzak/MCP-Plugin-dotnet 'Stars')](https://github.com/IvanMurzak/MCP-Plugin-dotnet/stargazers)
[![Discord](https://img.shields.io/badge/Discord-Join-7289da?logo=discord&logoColor=white&labelColor=333A41 'Join')](https://discord.gg/Cgs6nM8BPU)
[![License](https://img.shields.io/github/license/IvanMurzak/MCP-Plugin-dotnet?label=License&labelColor=333A41)](https://github.com/IvanMurzak/MCP-Plugin-dotnet/blob/main/LICENSE)
[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/badges/StandWithUkraine.svg)](https://stand-with-ukraine.pp.ua)

## MCP Plugin

**McpPlugin** is a .NET client-side library that enables .NET applications to integrate with Model Context Protocol (MCP) servers. It serves as a bridge between .NET applications and MCP servers, providing seamless communication over SignalR/ASP.NET Core.

### Key Features

- **Attribute-Based Registration**: Easily expose tools, prompts, and resources using `[McpPluginTool]`, `[McpPluginPrompt]`, and `[McpPluginResource]` attributes
- **Automatic Schema Generation**: JSON schemas are automatically generated from method signatures
- **Assembly Scanning**: Automatically discover and register components from assemblies
- **Robust Connection Management**: Built-in reconnection logic with configurable retry policies
- **Reactive Programming**: Event-driven architecture using R3 (Reactive Extensions)
- **SignalR Integration**: Real-time bidirectional communication with MCP servers
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Type-Safe Communication**: Strongly-typed request/response models

### Architecture

The library is structured in layers:

```text
Application Layer (Your Code)
        ↓
McpPlugin (Facade & Entry Point)
        ↓
Manager Layer (Tool/Prompt/Resource Managers)
        ↓
Transport Layer (SignalR Hub Connections)
        ↓
SignalR/ASP.NET Core
```

### Core Components

#### McpPlugin

Main orchestration class ([McpPlugin.cs:21-207](McpPlugin/McpPlugin.cs#L21-L207)) that coordinates all managers and handles connection lifecycle. Available as a singleton for easy access.

#### McpPluginBuilder

Fluent API builder ([McpPluginBuilder.cs:22-246](McpPlugin/Builder/McpPluginBuilder.cs#L22-L246)) for configuring the plugin:

- `WithTool()` - Register tool methods
- `WithPrompt()` - Register prompt methods
- `WithResource()` - Register resource handlers
- `Build()` - Construct the configured plugin instance

#### Manager Classes

- **McpToolManager** ([McpToolManager.cs:26-204](McpPlugin/Mcp/McpToolManager.cs#L26-L204)) - Manages tool execution and lifecycle
- **McpPromptManager** ([McpPromptManager.cs:23-166](McpPlugin/Mcp/McpPromptManager.cs#L23-L166)) - Manages prompt execution
- **McpResourceManager** ([McpResourceManager.cs:26-223](McpPlugin/Mcp/McpResourceManager.cs#L26-L223)) - Manages resource content with URI-based routing

#### Connection Management

- **ConnectionManager** ([ConnectionManager.cs:21-443](McpPlugin/Network/Connection/ConnectionManager.cs#L21-L443)) - Handles connection lifecycle with automatic reconnection
- **HubConnectionProvider** ([HubConnectionProvider.cs:21-71](McpPlugin/Network/Connection/Provider/HubConnectionProvider.cs#L21-L71)) - Creates and configures SignalR connections
- **McpManagerClientHub** - Handles bidirectional communication between client and server

### Usage Example

```csharp
// Configure and build the plugin
var plugin = new McpPluginBuilder()
    .WithTool("GetWeather", "Get weather for a city",
        async (string city) => $"Weather in {city}: Sunny")
    .WithToolsFromAssembly(typeof(MyTools).Assembly)
    .Build();

// Connect to the MCP server
await plugin.Connect();

// Tool methods with [McpPluginTool] attribute are automatically discovered
public class MyTools
{
    [McpPluginTool(Title = "Calculate Sum", Description = "Adds two numbers")]
    public static int Add(int a, int b) => a + b;
}
```

### Configuration

The plugin uses `ConnectionConfig` for server connection settings:

- **ServerUrl**: MCP server endpoint (default: configured via dependency injection)
- **TimeoutMs**: Operation timeout in milliseconds
- **Keep-Alive**: 30-second interval
- **Server Timeout**: 5 minutes
- **Retry Policy**: Fixed 10-second intervals with automatic reconnection

### Communication Model

The plugin supports bidirectional communication:

**Client → Server:**

- `NotifyAboutUpdatedTools()` - Notify server of tool changes
- `NotifyAboutUpdatedPrompts()` - Notify server of prompt changes
- `NotifyAboutUpdatedResources()` - Notify server of resource changes

**Server → Client:**

- `RunCallTool` - Execute a tool with parameters
- `RunListTool` - List available tools
- `RunGetPrompt` - Get prompt content
- `RunListPrompts` - List available prompts
- `RunResourceContent` - Get resource content by URI
- `RunListResources` - List available resources
- `RunResourceTemplates` - List resource templates

### Technology Stack

- **.NET Standard 2.1** - Cross-platform support
- **SignalR** - Real-time communication
- **R3** - Reactive extensions for event handling
- **ReflectorNet** - Reflection utilities for method invocation and schema generation
- **System.Text.Json** - JSON serialization
- **Microsoft.Extensions.DependencyInjection** - Service container

### Target Framework

netstandard2.1 - Compatible with .NET Core 3.0+, .NET 5+, and .NET Framework 4.8+

## MCP Server

**McpPlugin.Server** is a standalone .NET server application that acts as a bridge between Model Context Protocol (MCP) clients and .NET applications integrated with the McpPlugin library. It enables AI assistants to interact with .NET applications through the MCP standard.

### Key Features

- **Dual Transport Support**: Supports both STDIO and HTTP transport methods for MCP communication
- **SignalR Bridge**: Real-time bidirectional communication with .NET applications via SignalR
- **.NET Tool Package**: Installable as a global .NET tool for easy deployment
- **Automatic Routing**: Routes MCP requests (tools, prompts, resources) to connected .NET applications
- **Event-Driven Architecture**: Reactive notifications for dynamic tool/prompt/resource updates
- **Session Management**: Per-session execution context for HTTP transport mode
- **Robust Logging**: Comprehensive NLog-based logging with file rotation

### Installation

Install as a global .NET tool:

```bash
dotnet tool install --global com.IvanMurzak.McpPlugin.Server
```

Or run directly:

```bash
dotnet run --project McpPlugin.Server
```

### Usage

The server can be configured via command-line arguments:

```bash
mcp-plugin-server --port=8080 --client-transport=stdio --plugin-timeout=10000
```

**Configuration Options:**

- `--port` - Server port for SignalR connections (default: 8080)
- `--client-transport` - MCP client transport: `stdio` or `http` (default: `stdio`)
- `--plugin-timeout` - Plugin connection timeout in milliseconds (default: 10000)

### Architecture

```text
MCP Client (AI Assistant)
        ↓
    STDIO/HTTP
        ↓
McpPlugin.Server (Bridge)
        ↓
    SignalR/ASP.NET Core
        ↓
.NET Application + McpPlugin
```

### Core Components

#### McpServerService

Hosted service ([McpServerService.cs](McpPlugin.Server/src/McpServerService.cs)) that manages the MCP server lifecycle and coordinates events between the MCP client and connected .NET applications.

#### Routers

Request routers forward MCP operations to connected applications:

- **ToolRouter** - Routes `tools/call` and `tools/list` requests
- **PromptRouter** - Routes `prompts/get` and `prompts/list` requests
- **ResourceRouter** - Routes `resources/read`, `resources/list`, and `resources/templates` requests

#### McpServerHub

SignalR hub ([McpServerHub.cs](McpPlugin.Server/src/Hub/McpServerHub.cs)) that manages connections from .NET applications and handles:

- Version handshake validation
- Tool/prompt/resource update notifications
- Request completion tracking

### Transport Modes

#### STDIO Transport

Standard input/output communication for MCP clients:

```bash
mcp-plugin-server --client-transport stdio
```

- Console logs redirected to stderr to avoid polluting stdout
- Ideal for Claude Desktop and similar MCP clients

#### HTTP Transport

HTTP-based communication with stateful sessions:

```bash
mcp-plugin-server --client-transport http --port 8080
```

- Per-session execution context
- Stateless mode disabled for persistent connections
- Accessible at `http://localhost:8080/` or `http://localhost:8080/mcp`

### Configuration Files

#### appsettings.json

Basic ASP.NET Core configuration for logging and allowed hosts.

#### NLog.config

Comprehensive logging configuration:

- File logging with automatic rotation (10 MB max per file)
- Console logging with color-coded levels
- Separate error log file for Error/Fatal levels
- Microsoft-style log formatting

#### server.json

MCP server metadata configuration compatible with Model Context Protocol registry schema.

### Technology Stack

- **.NET 9.0** - Latest .NET framework with native AOT support potential
- **ASP.NET Core** - Web framework for HTTP endpoints
- **SignalR** - Real-time bidirectional communication
- **ModelContextProtocol** (v0.3.0-preview.4) - Official MCP SDK
- **ModelContextProtocol.AspNetCore** - ASP.NET Core integration for MCP
- **R3** - Reactive extensions for event handling
- **NLog** - Structured logging framework
- **ReflectorNet** - Reflection utilities

### Deployment

The server is packaged as a .NET tool and can be:

- Installed globally: `dotnet tool install -g com.IvanMurzak.McpPlugin.Server`
- Run as executable: `mcp-plugin-server`
- Containerized using the included Dockerfile
- Integrated into CI/CD pipelines

### Version Compatibility

The server performs API version handshaking with connected plugins to ensure compatibility:

- API Version: `1.0.0` (defined in `Consts.ApiVersion`)
- Exact version match required for successful connection
- Version mismatch errors logged and rejected

### Endpoints

**SignalR Hub:** `/mcp-remote-app` - Main SignalR endpoint for plugin connections

**HTTP Transport Mode:**

- `/` - Primary MCP endpoint
- `/mcp` - Alternative MCP endpoint
- `/help` - Informational endpoint with usage details

### Example: Claude Desktop Integration

Add to Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "dotnet-app": {
      "command": "mcp-plugin-server",
      "args": ["client-transport=stdio", "port=8080"]
    }
  }
}
```
