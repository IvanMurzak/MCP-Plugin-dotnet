# Research: Analytics Webhooks for McpPlugin.Server

**Branch**: `001-analytics-webhooks` | **Date**: 2026-03-01

---

## R1: Webhook Dispatch Architecture

### Decision: `Channel<WebhookMessage>` + `BackgroundService` + `IHttpClientFactory` (Named Client)

### Rationale

The ≤10 ms p99 latency requirement makes fire-and-forget via `_ = Task.Run(...)` unacceptable — it causes thread pool saturation under burst load and provides no exception observability. A `Channel<T>` backed by a `BackgroundService` is the standard .NET pattern for this:

- `Channel.CreateBounded<T>` with `FullMode = DropOldest` — producer (router) never blocks, writes complete in sub-microsecond time
- `BackgroundService` consumer owns all HTTP I/O — exception handling, timeout, logging all happen off the request path
- `IHttpClientFactory` with a named client (`"webhook"`) avoids the singleton-captures-transient antipattern and benefits from handler rotation + DNS refresh

### Alternatives Considered

| Alternative | Rejected Because |
|---|---|
| `_ = DispatchAsync()` (bare fire-and-forget) | Swallowed exceptions, thread pool saturation, no graceful shutdown, untestable |
| `Task.Run` per event | Same issues plus scoped service disposal risk |
| Typed `HttpClient` | Captured by singleton `BackgroundService`, defeating handler rotation |
| `System.Reactive` / R3 Subject pipeline | Constitution Principle V allows R3 but Channel is more appropriate for a producer-consumer queue; R3 `Subject` adds no value over `Channel` for fire-and-forget HTTP POST dispatch |
| Unbounded channel | No backpressure — under extreme load, memory grows without bound |

### Design

```
Router/Hub (hot path)           Background (cold path)
─────────────────────           ──────────────────────
Serialize payload to JSON  ──►  Channel<WebhookMessage>  ──►  BackgroundService
(sub-μs channel write)                                          ├─ CreateClient("webhook")
                                                                ├─ POST to URL
                                                                ├─ Log success/failure
                                                                └─ No retry
```

- **Channel capacity**: 1024 (configurable). `DropOldest` under backpressure — acceptable for analytics.
- **SingleReader = true**: Enables optimized internal channel paths.
- **HTTP timeout**: Configurable via `DataArguments` (default 10s). Set as `HttpClient.Timeout` in the named client config.
- **ConnectTimeout**: 2s via `SocketsHttpHandler.ConnectTimeout` to fast-fail on unreachable hosts.

---

## R2: DataArguments Extension Pattern

### Decision: Follow existing `DataArguments` pattern exactly — constants in `Consts.MCP.Server.Args/Env`, properties in `IDataArguments`/`DataArguments`, parsed in `ParseEnvironmentVariables()` then `ParseCommandLineArguments()`

### Rationale

The existing pattern is well-established, tested, and consistent. All configuration flows through `DataArguments` → injected as `IDataArguments` singleton. No reason to deviate.

### New Parameters

| CLI Arg | Env Var | Type | Default | Description |
|---|---|---|---|---|
| `webhook-tool-url` | `MCP_PLUGIN_WEBHOOK_TOOL_URL` | `string?` | `null` | Tool call event webhook URL |
| `webhook-prompt-url` | `MCP_PLUGIN_WEBHOOK_PROMPT_URL` | `string?` | `null` | Prompt event webhook URL |
| `webhook-resource-url` | `MCP_PLUGIN_WEBHOOK_RESOURCE_URL` | `string?` | `null` | Resource event webhook URL |
| `webhook-connection-url` | `MCP_PLUGIN_WEBHOOK_CONNECTION_URL` | `string?` | `null` | Connection event webhook URL |
| `webhook-token` | `MCP_PLUGIN_WEBHOOK_TOKEN` | `string?` | `null` | Security token value |
| `webhook-header` | `MCP_PLUGIN_WEBHOOK_HEADER` | `string?` | `X-Webhook-Token` | Header name for token |
| `webhook-timeout` | `MCP_PLUGIN_WEBHOOK_TIMEOUT` | `int` | `10000` | HTTP timeout in ms |

### Parsing Pattern

```csharp
// Env vars (lower priority, parsed first)
var envWebhookToolUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookToolUrl);
if (envWebhookToolUrl != null)
    WebhookToolUrl = envWebhookToolUrl;

// CLI args (higher priority, parsed second, overrides env)
var argWebhookToolUrl = commandLineArgs.GetValueOrDefault(
    Consts.MCP.Server.Args.WebhookToolUrl.TrimStart('-'));
if (argWebhookToolUrl != null)
    WebhookToolUrl = argWebhookToolUrl;
```

### Startup Warnings

At server startup (in extension method or hosted service):
- If any webhook URL uses `http://` (not `https://`): log warning about token transmitted without TLS
- If any webhook URL is configured but `webhook-token` is not set: log warning about missing token
- If no webhook URLs are configured: silent — no warnings, no errors

---

## R3: Router Interception Strategy

### Decision: Wrap router handler delegates in `ExtensionsMcpServer.cs` with timing/size measurement, then enqueue webhook events via `IWebhookEventCollector` resolved from `request.Services`

### Rationale

Routers are static partial classes. The handler registration in `ExtensionsMcpServer.cs` assigns static method references:

```csharp
options.Handlers.CallToolHandler = ToolRouter.Call;
```

Rather than modifying the static methods directly (which would couple router logic to webhook concerns), we wrap them at the registration point:

```csharp
options.Handlers.CallToolHandler = async (request, ct) =>
{
    var stopwatch = Stopwatch.StartNew();
    var requestSize = MeasureRequestSize(request.Params);

    var result = await ToolRouter.Call(request, ct);

    stopwatch.Stop();
    var responseSize = MeasureResponseSize(result);

    var collector = request.Services.GetService<IWebhookEventCollector>();
    collector?.OnToolCall(request.Params.Name, requestSize, responseSize,
        result.IsError ? "failure" : "success", stopwatch.ElapsedMilliseconds,
        result.IsError ? ExtractErrorMessage(result) : null);

    return result;
};
```

### Data Access Points

| Field | Tool | Prompt | Resource |
|---|---|---|---|
| Name/URI | `request.Params.Name` | `request.Params.Name` | `request.Params.Uri` |
| Request size | Serialize `request.Params.Arguments` | Serialize `request.Params.Arguments` | N/A (URI only) |
| Response size | Serialize `CallToolResult` | Serialize `GetPromptResult` | Serialize `ReadResourceResult` |
| Duration | `Stopwatch` around full call | `Stopwatch` around full call | `Stopwatch` around full call |
| Status | `result.IsError` | `response.Status` | `response.Status` |
| Error details | `result.Content` text | `response.Message` | `response.Message` |

### Size Measurement

```csharp
var json = JsonSerializer.Serialize(obj, JsonOptions.Pretty);
var sizeBytes = Encoding.UTF8.GetByteCount(json);
```

Uses existing `JsonOptions.Pretty` from `McpPlugin.Common`. This gives wire-format byte count as specified in the requirements.

---

## R4: Connection Lifecycle Interception

### Decision: Inject `IWebhookEventCollector` into `McpServerHub` (for plugin connect/disconnect) and `McpServerService` (for AI agent connect/disconnect)

### Rationale

Two independent connection types exist:

**AI Agent (MCP Client)** — lifecycle managed by `McpServerService`:
- `StartAsync()` → AI agent connected (after MCP initialize handshake)
- `StopAsync()` → AI agent disconnected
- Metadata: `McpServer.ClientInfo` → `Name`, `Version`, `Title`, `Description`, `WebsiteUrl`
- Session ID: `McpServer.SessionId` (MCP protocol UUID)

**Plugin (.NET App)** — lifecycle managed by `BaseHub<T>` → `McpServerHub`:
- `OnConnectedAsync()` → plugin connected
- `OnDisconnectedAsync()` → plugin disconnected
- Metadata: sent during SignalR handshake (version handshake data)
- Session ID: `Context.ConnectionId` (SignalR connection ID)

### Integration Points

**For AI Agent events** — `McpServerService` receives `IWebhookEventCollector` via constructor DI:
```csharp
// In NotifyClientConnectedAsync (after existing logic):
_webhookCollector?.OnAiAgentConnected(sessionId, clientName, clientVersion, metadata);

// In NotifyClientDisconnectedAsync:
_webhookCollector?.OnAiAgentDisconnected(sessionId);
```

**For Plugin events** — `McpServerHub` receives `IWebhookEventCollector` via constructor DI:
```csharp
// In OnConnectedAsync (after base call):
_webhookCollector?.OnPluginConnected(connectionId, pluginMetadata);

// In OnDisconnectedAsync (after base call):
_webhookCollector?.OnPluginDisconnected(connectionId);
```

### Plugin Metadata

Plugin metadata is available after version handshake (`PerformVersionHandshake`), not at connect time. Two options:
1. Emit connect event immediately with connection ID only, then emit metadata-enriched event after handshake
2. Defer connect event until after version handshake completes

**Decision**: Option 1 — emit immediately with available data. The connection event spec says "all available McpPlugin metadata" which means whatever is available at the time. Connection ID is always available; additional metadata (name, version) arrives after handshake and can be included if a brief deferral is acceptable, but the event must still fire even if no metadata arrives.

---

## R5: Serialization & Payload Format

### Decision: Use `System.Text.Json` with `JsonSerializerOptions` consistent with existing `JsonOptions.Pretty` but with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`

### Rationale

The codebase already uses `System.Text.Json` everywhere. Webhook payloads should use `camelCase` property names (standard for JSON APIs consumed by external systems), unlike the internal SignalR protocol which preserves C# `PascalCase`.

### Payload Envelope

```json
{
  "schemaVersion": "1.0",
  "eventType": "tool.call.completed",
  "timestamp": "2026-03-01T12:34:56.789Z",
  "data": { ... }
}
```

### Event Type Discriminators

| Event | `eventType` value |
|---|---|
| Tool call completed (success) | `tool.call.completed` |
| Tool call completed (failure) | `tool.call.completed` |
| Prompt retrieved | `prompt.retrieved` |
| Resource accessed | `resource.accessed` |
| AI agent connected | `connection.ai-agent.connected` |
| AI agent disconnected | `connection.ai-agent.disconnected` |
| Plugin connected | `connection.plugin.connected` |
| Plugin disconnected | `connection.plugin.disconnected` |

---

## R6: Security Considerations

### Token in Headers

- Token is sent as the value of a configurable header (default `X-Webhook-Token`)
- Token MUST NOT appear in any log output (FR-014)
- Logging must redact token values — log the header name but not the value

### HTTP Warning

- At startup, if any configured URL uses `http://` scheme, log:
  `"WARNING: Webhook URL '{url}' uses HTTP (non-TLS). Security token will be transmitted without encryption."`

### Input Validation

- Webhook URLs are validated at startup: must be well-formed absolute URIs with `http` or `https` scheme
- Invalid URLs cause a startup warning (not a hard error) and the webhook category is disabled

---

## R7: DI Registration Strategy

### Decision: Extension method `AddWebhooks(IDataArguments)` on `IServiceCollection`, called from `WithMcpPluginServer`

### Registration Order

```csharp
// In ExtensionsMcpServerBuilder.WithMcpPluginServer():
mcpServerBuilder.Services.AddWebhooks(dataArguments);
```

### Services Registered

| Service | Lifetime | Purpose |
|---|---|---|
| `WebhookOptions` | Singleton | Parsed from `IDataArguments` — URLs, token, header, timeout |
| `IWebhookQueue` / `WebhookQueue` | Singleton | `Channel<WebhookMessage>` producer interface |
| `WebhookDispatchService` | Hosted | `BackgroundService` consumer — reads channel, POSTs to URLs |
| `IWebhookEventCollector` / `WebhookEventCollector` | Singleton | Accepts domain events, serializes, enqueues |
| Named `HttpClient` (`"webhook"`) | Factory | Configured timeout, User-Agent, no default auth |

### Conditional Registration

If no webhook URLs are configured (all null), the entire webhook subsystem is skipped:
- No `HttpClient` registered
- No `BackgroundService` started
- `IWebhookEventCollector` resolves to a no-op implementation
- Zero runtime overhead when webhooks are disabled

---

## Open Questions — All Resolved

| # | Question | Resolution |
|---|---|---|
| 1 | Channel vs Task.Run for dispatch | Channel + BackgroundService (R1) |
| 2 | Named vs typed HttpClient | Named client (R1) |
| 3 | How to extend DataArguments | Follow existing pattern (R2) |
| 4 | Where to intercept router calls | Wrapper delegates in ExtensionsMcpServer (R3) |
| 5 | Where to intercept connection events | McpServerHub + McpServerService constructors (R4) |
| 6 | Payload serialization format | System.Text.Json with camelCase (R5) |
| 7 | Plugin metadata timing | Emit immediately with available data (R4) |
| 8 | Disabled webhooks overhead | No-op collector, no services registered (R7) |
