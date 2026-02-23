# Transport + Auth Architecture

## Overview

The MCP Plugin Server is configured through **two independent axes**: **Transport** and **Auth**.
Each axis has its own enum, CLI argument, and environment variable. They combine freely — any transport works with any auth option.

```
Transport  ×  Auth  ×  Token
   stdio          none        (optional)
   streamableHttp required    (required when auth=required)
```

All 4 combinations (2 transports × 2 auth options) are supported without conditional branching in the server code. The correct behavior is selected automatically at startup via the **Strategy Pattern** and **Transport Layer Pattern**.

---

## Configuration

### Transport — how MCP clients (e.g. Claude Desktop) connect

| Source           | Key                                   | Values                        |
|------------------|---------------------------------------|-------------------------------|
| CLI argument     | `--client-transport=<value>`          | `stdio`, `streamableHttp`     |
| Environment var  | `MCP_PLUGIN_CLIENT_TRANSPORT=<value>` | `stdio`, `streamableHttp`     |

**Default**: `stdio` (when not set)

### Auth — how plugin connections are authenticated and isolated

| Source           | Key                          | Values              |
|------------------|------------------------------|---------------------|
| CLI argument     | `--authorization=<value>`    | `none`, `required`  |
| Environment var  | `MCP_AUTHORIZATION=<value>`  | `none`, `required`  |

**Default**: `none` (when not set)

### Token — the shared secret used by `auth=required`

| Source           | Key                       | Example                     |
|------------------|---------------------------|-----------------------------|
| CLI argument     | `--token=<value>`         | `--token=mySecret`          |
| Environment var  | `MCP_PLUGIN_TOKEN=<value>`| `MCP_PLUGIN_TOKEN=mySecret` |

**Optional at server launch** when `authorization=required`. When absent, the server enters dynamic-pairing mode — any plugin token is accepted and plugins/clients are paired by token equality. When present, only that exact token is accepted from connecting plugins. Ignored (but accepted) when `authorization=none`. Note: connecting plugins must always provide a token in `auth=required` mode regardless.

---

## Connection Strategy

The `IMcpConnectionStrategy` interface is the central coordinator for all auth-dependent behavior. A concrete strategy is resolved once at startup by `McpStrategyFactory` based on the configured `AuthOption` and injected as a singleton throughout the server.

```
McpStrategyFactory
  ├── AuthOption.none     → NoAuthMcpStrategy
  └── AuthOption.required → RequiredAuthMcpStrategy
```

### NoAuthMcpStrategy (`authorization=none`)

Designed for a **single trusted plugin** connecting to a single MCP client (e.g. a developer's local machine running one Unity editor). Authentication is optional — a token may be provided for extra protection, but it does not enable multi-tenancy.

### RequiredAuthMcpStrategy (`authorization=required`)

Designed for **multiple independent plugins** each owning a specific MCP client session. Every plugin connection must carry a unique Bearer token. Notifications and data queries are scoped strictly to the matching session.

---

## Behavior Comparison

### Core properties

| Property                   | `NoAuthMcpStrategy`           | `RequiredAuthMcpStrategy`         |
|----------------------------|-------------------------------|-----------------------------------|
| `AuthOption`               | `none`                        | `required`                        |
| `AllowMultipleConnections` | `false`                       | `true`                            |
| Token required?            | No (optional)                 | No — plugins must provide a token |

### `Validate(dataArguments)`

| Strategy                   | Behavior                                                                      |
|----------------------------|-------------------------------------------------------------------------------|
| `NoAuthMcpStrategy`        | No-op. Token is optional.                                                     |
| `RequiredAuthMcpStrategy`  | No-op. Server token is optional; enables dynamic-pairing when absent.         |

### `ConfigureAuthentication(options, dataArguments)`

| Strategy                   | `options.RequireToken`                    | `options.ServerToken`   |
|----------------------------|-------------------------------------------|-------------------------|
| `NoAuthMcpStrategy`        | `true` only if token is set, else `false` | Token value or `null`   |
| `RequiredAuthMcpStrategy`  | Always `true`                             | Token value or `null`   |

### `OnPluginConnected(hubType, connectionId, token, logger, disconnectClient)`

| Strategy                   | Behavior                                                                                                    |
|----------------------------|-------------------------------------------------------------------------------------------------------------|
| `NoAuthMcpStrategy`        | Registers the new connection, then **disconnects all other** existing connections (single-connection rule). |
| `RequiredAuthMcpStrategy`  | Registers the new connection. Existing connections are **not disturbed**.                                   |

### `OnPluginDisconnected(hubType, connectionId, logger)`

Both strategies remove the connection from the registry. No difference.

### `ResolveConnectionId(token, retryOffset)`

| Strategy                   | Behavior                                                                                                          |
|----------------------------|-------------------------------------------------------------------------------------------------------------------|
| `NoAuthMcpStrategy`        | Looks up by token first; falls back to round-robin across all connections.                                        |
| `RequiredAuthMcpStrategy`  | Looks up by token first (primary path); falls back to round-robin as a safety net if the token lookup misses.    |

In practice `NoAuthMcpStrategy` hits the round-robin fallback often (token is optional), while `RequiredAuthMcpStrategy` always has a token and almost always resolves by direct lookup.

### `ShouldNotifySession(pluginConnectionId, sessionId)`

This guards every MCP notification (tool-list changed, prompt-list changed, resource-list changed) from reaching the wrong MCP client session.

| Strategy                   | Logic                                                                                                     | Result                                   |
|----------------------------|-----------------------------------------------------------------------------------------------------------|------------------------------------------|
| `NoAuthMcpStrategy`        | Always returns `true` — broadcast to every session.                                                       | All MCP clients see every notification.  |
| `RequiredAuthMcpStrategy`  | Looks up the plugin's token; returns `true` only if it equals `sessionId`.                                | Each MCP client only sees its own plugin's notifications. |

### `GetClientData(connectionId, sessionTracker)` / `GetServerData(...)`

| Strategy                   | Scoping                                                                                    |
|----------------------------|--------------------------------------------------------------------------------------------|
| `NoAuthMcpStrategy`        | Returns the **first available** session data — no scoping needed (only one session).       |
| `RequiredAuthMcpStrategy`  | Looks up the plugin's token from `connectionId`, then retrieves data **scoped to that token**. Falls back to unscoped if no token. |

---

## Combination Matrix

All 4 combinations work. The table shows the expected use case for each.

| Transport        | Auth       | Token     | Typical use case                                                                              |
|------------------|------------|-----------|-----------------------------------------------------------------------------------------------|
| `stdio`          | `none`     | —         | **Local dev, single user.** Claude Desktop spawns the server as a subprocess. Simple setup, no auth needed. |
| `stdio`          | `none`     | set       | Local dev with optional transport-level token protection. One plugin, one Claude session.     |
| `stdio`          | `required` | required  | Secure connection between MCP server and MCP plugin. Single MCP client via Stdio, because it is single-session by nature.    |
| `streamableHttp` | `none`     | —         | **Local HTTP server, single plugin.** Useful for testing HTTP transport without auth.         |
| `streamableHttp` | `none`     | set       | Single plugin via HTTP with a basic shared secret for protection.                             |
| `streamableHttp` | `required` | required  | **Multi-tenant / remote deployment.** Many .NET apps each connect with their own unique token. Each is isolated to its own Claude session. |

---

## Architecture Flow

```
Startup
  DataArguments.ClientTransport ──► TransportFactory ──► ITransportLayer
                                      (stdio / streamableHttp)

  DataArguments.Authorization ────► McpStrategyFactory ──► IMcpConnectionStrategy
                                      (none / required)

Both singletons are registered in DI and flow through the entire server:

  BaseHub           ──uses──► IMcpConnectionStrategy (OnPluginConnected / OnPluginDisconnected)
  McpServerHub      ──uses──► IMcpConnectionStrategy (GetClientData / GetServerData)
  McpServerService  ──uses──► IMcpConnectionStrategy (ShouldNotifySession)
  RemoteToolRunner  ──uses──► IMcpConnectionStrategy (ResolveConnectionId via ClientUtils.InvokeAsync)
  RemotePromptRunner──uses──► IMcpConnectionStrategy
  RemoteResourceRunner──uses►IMcpConnectionStrategy
```

---

## Example Launch Commands

```bash
# Local development (stdio, no auth) — typical Claude Desktop config
dotnet run --client-transport=stdio

# Local HTTP server without auth
dotnet run --client-transport=streamableHttp --port=8080

# Local HTTP server with a static token
dotnet run --client-transport=streamableHttp --port=8080 --token=mySecret

# Multi-tenant remote deployment
dotnet run --client-transport=streamableHttp --port=8080 --authorization=required --token=sharedSecret
```

For remote multi-tenant use, each .NET plugin provides **its own** Bearer token when connecting:

```csharp
// In the .NET app (plugin side)
new McpPluginBuilder(version)
    .WithConfig(config => {
        config.Host  = "http://my-server:8080";
        config.Token = "uniqueTokenForThisApp";
    })
    .Build(reflector);
```

The server routes all MCP requests and notifications for that token exclusively to that plugin instance.
