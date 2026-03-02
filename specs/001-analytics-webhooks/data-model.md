# Data Model: Analytics Webhooks

**Branch**: `001-analytics-webhooks` | **Date**: 2026-03-01

---

## Entity Relationship Overview

```
WebhookOptions (singleton config)
├── ToolWebhookUrl?
├── PromptWebhookUrl?
├── ResourceWebhookUrl?
├── ConnectionWebhookUrl?
├── TokenValue?
├── HeaderName
└── TimeoutMs

WebhookPayload<T> (envelope)
├── SchemaVersion: string ("1.0")
├── EventType: string (discriminator)
├── Timestamp: DateTimeOffset (UTC)
└── Data: T (one of the event types below)

ToolCallEvent
├── ToolName: string
├── RequestSizeBytes: long
├── ResponseSizeBytes: long
├── Status: string ("success" | "failure")
├── DurationMs: long
└── ErrorDetails: string? (only on failure)

PromptEvent
├── PromptName: string
└── ResponseSizeBytes: long

ResourceEvent
├── ResourceUri: string
└── ResponseSizeBytes: long

ConnectionEvent
├── EventType: string (discriminator)
├── ClientType: string ("ai-agent" | "plugin")
├── SessionId: string
├── ClientName: string?
├── ClientVersion: string?
└── Metadata: Dictionary<string, string>?

WebhookMessage (internal queue item)
├── TargetUrl: string
├── JsonPayload: string (pre-serialized)
├── HeaderName: string?
└── TokenValue: string?
```

---

## Entity Definitions

### WebhookOptions

Configuration parsed from `DataArguments` at startup. Immutable after construction.

| Field | Type | Default | Source (CLI) | Source (Env) |
|---|---|---|---|---|
| `ToolWebhookUrl` | `string?` | `null` | `webhook-tool-url` | `MCP_PLUGIN_WEBHOOK_TOOL_URL` |
| `PromptWebhookUrl` | `string?` | `null` | `webhook-prompt-url` | `MCP_PLUGIN_WEBHOOK_PROMPT_URL` |
| `ResourceWebhookUrl` | `string?` | `null` | `webhook-resource-url` | `MCP_PLUGIN_WEBHOOK_RESOURCE_URL` |
| `ConnectionWebhookUrl` | `string?` | `null` | `webhook-connection-url` | `MCP_PLUGIN_WEBHOOK_CONNECTION_URL` |
| `TokenValue` | `string?` | `null` | `webhook-token` | `MCP_PLUGIN_WEBHOOK_TOKEN` |
| `HeaderName` | `string` | `X-Webhook-Token` | `webhook-header` | `MCP_PLUGIN_WEBHOOK_HEADER` |
| `TimeoutMs` | `int` | `10000` | `webhook-timeout` | `MCP_PLUGIN_WEBHOOK_TIMEOUT` |

**Validation rules**:
- URLs must be well-formed absolute URIs with `http` or `https` scheme (if provided)
- Invalid URLs are logged as warnings and treated as unconfigured (null)
- `TimeoutMs` must be > 0; invalid values fall back to default (10000)
- `HeaderName` must be a valid HTTP header name; invalid values fall back to default

**Computed properties**:
- `IsEnabled` → `true` if any webhook URL is non-null
- `IsToolEnabled` → `ToolWebhookUrl != null`
- `IsPromptEnabled` → `PromptWebhookUrl != null`
- `IsResourceEnabled` → `ResourceWebhookUrl != null`
- `IsConnectionEnabled` → `ConnectionWebhookUrl != null`
- `HasToken` → `TokenValue != null`

---

### WebhookPayload\<T\>

JSON envelope wrapping every outgoing webhook HTTP POST body. Generic over the event data type.

| Field | Type | JSON Name | Description |
|---|---|---|---|
| `SchemaVersion` | `string` | `schemaVersion` | Always `"1.0"` for initial release |
| `EventType` | `string` | `eventType` | Discriminator (see table below) |
| `Timestamp` | `DateTimeOffset` | `timestamp` | UTC timestamp of event occurrence |
| `Data` | `T` | `data` | Event-specific payload |

**Event type discriminators**:

| Event | `eventType` Value |
|---|---|
| Tool call completed | `tool.call.completed` |
| Prompt retrieved | `prompt.retrieved` |
| Resource accessed | `resource.accessed` |
| AI agent connected | `connection.ai-agent.connected` |
| AI agent disconnected | `connection.ai-agent.disconnected` |
| Plugin connected | `connection.plugin.connected` |
| Plugin disconnected | `connection.plugin.disconnected` |

**Serialization**: `System.Text.Json` with `camelCase` naming policy, `DefaultIgnoreCondition = WhenWritingNull`.

---

### ToolCallEvent

Analytics record emitted after every MCP tool call, regardless of outcome.

| Field | Type | JSON Name | Description |
|---|---|---|---|
| `ToolName` | `string` | `toolName` | Name of the invoked tool |
| `RequestSizeBytes` | `long` | `requestSizeBytes` | UTF-8 byte count of serialized request arguments |
| `ResponseSizeBytes` | `long` | `responseSizeBytes` | UTF-8 byte count of serialized response (0 on failure with no response) |
| `Status` | `string` | `status` | `"success"` or `"failure"` |
| `DurationMs` | `long` | `durationMs` | Full round-trip ms (server receive → server send) |
| `ErrorDetails` | `string?` | `errorDetails` | Error message/type on failure; null on success |

**State transitions**: N/A (immutable record, created once per tool call).

---

### PromptEvent

Analytics record emitted when a prompt is retrieved.

| Field | Type | JSON Name | Description |
|---|---|---|---|
| `PromptName` | `string` | `promptName` | Name/ID of the retrieved prompt |
| `ResponseSizeBytes` | `long` | `responseSizeBytes` | UTF-8 byte count of serialized prompt content |

---

### ResourceEvent

Analytics record emitted when a resource is accessed.

| Field | Type | JSON Name | Description |
|---|---|---|---|
| `ResourceUri` | `string` | `resourceUri` | Resource URI or matched URI template identifier |
| `ResponseSizeBytes` | `long` | `responseSizeBytes` | UTF-8 byte count of serialized resource content |

---

### ConnectionEvent

Analytics record emitted for client connection lifecycle transitions.

| Field | Type | JSON Name | Description |
|---|---|---|---|
| `EventType` | `string` | `eventType` | One of: `connected`, `disconnected` |
| `ClientType` | `string` | `clientType` | `"ai-agent"` or `"plugin"` |
| `SessionId` | `string` | `sessionId` | MCP session UUID (AI agent) or SignalR connection ID (plugin) |
| `ClientName` | `string?` | `clientName` | From `ClientInfo.Name` (AI agent) or plugin registration (plugin) |
| `ClientVersion` | `string?` | `clientVersion` | From `ClientInfo.Version` (AI agent) or plugin version (plugin) |
| `Metadata` | `Dictionary<string, string>?` | `metadata` | Additional handshake data; omitted if empty |

**Metadata sources**:
- AI agent: `ClientInfo.Title`, `ClientInfo.Description`, `ClientInfo.WebsiteUrl` (if provided)
- Plugin: version handshake data (if available at event time)

---

### WebhookMessage (Internal)

Internal queue item — not exposed externally. Passed through `Channel<WebhookMessage>` from collector to dispatcher.

| Field | Type | Description |
|---|---|---|
| `TargetUrl` | `string` | Destination webhook URL |
| `JsonPayload` | `string` | Pre-serialized JSON (WebhookPayload envelope) |
| `HeaderName` | `string?` | Security header name (null if no token) |
| `TokenValue` | `string?` | Security token value (null if not configured) |

**Rationale for pre-serialization**: Serialization happens on the hot path (router thread) but is cheap relative to HTTP I/O. Pre-serializing ensures the `BackgroundService` consumer does zero reflection/serialization work — just raw bytes to `StringContent`.

---

## C# Type Mapping

| Entity | C# Type | Location |
|---|---|---|
| `WebhookOptions` | `sealed class` | `McpPlugin.Server/src/Webhooks/Config/WebhookOptions.cs` |
| `WebhookPayload<T>` | `sealed class` | `McpPlugin.Server/src/Webhooks/Models/WebhookPayload.cs` |
| `ToolCallEvent` | `sealed class` | `McpPlugin.Server/src/Webhooks/Models/ToolCallEvent.cs` |
| `PromptEvent` | `sealed class` | `McpPlugin.Server/src/Webhooks/Models/PromptEvent.cs` |
| `ResourceEvent` | `sealed class` | `McpPlugin.Server/src/Webhooks/Models/ResourceEvent.cs` |
| `ConnectionEvent` | `sealed class` | `McpPlugin.Server/src/Webhooks/Models/ConnectionEvent.cs` |
| `WebhookMessage` | `sealed record` | `McpPlugin.Server/src/Webhooks/Services/WebhookMessage.cs` |

All model classes use `System.Text.Json` attributes (`[JsonPropertyName]`) for explicit camelCase mapping. No reliance on naming policy at the model level — explicit is better than implicit for a public contract.
