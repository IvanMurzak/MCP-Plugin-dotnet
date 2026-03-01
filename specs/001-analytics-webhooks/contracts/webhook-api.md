# Webhook HTTP API Contract

**Version**: 1.0 | **Date**: 2026-03-01

This document defines the HTTP contract between McpPlugin.Server (producer) and external webhook consumers.

---

## Transport

- **Method**: `POST`
- **Content-Type**: `application/json; charset=utf-8`
- **Encoding**: UTF-8
- **Timeout**: Configurable (default 10s). Requests exceeding timeout are cancelled.
- **Retry**: None. Fire-and-forget — single delivery attempt per event.
- **TLS**: Both HTTP and HTTPS accepted. HTTP URLs trigger a startup warning.

---

## Authentication

- **Header**: Configurable name (default `X-Webhook-Token`)
- **Value**: Opaque string token configured at launch
- **Behavior when no token configured**: Header is omitted entirely

Example:
```
POST /webhooks/mcp-tools HTTP/1.1
Host: analytics.example.com
Content-Type: application/json; charset=utf-8
X-Webhook-Token: my-secret-token-123

{ ... payload ... }
```

---

## Payload Envelope

Every webhook POST body follows this envelope structure:

```json
{
  "schemaVersion": "1.0",
  "eventType": "<discriminator>",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": { ... }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `schemaVersion` | `string` | Yes | Payload format version. Initial: `"1.0"` |
| `eventType` | `string` | Yes | Event type discriminator (see below) |
| `timestamp` | `string` (ISO 8601) | Yes | UTC timestamp of event occurrence |
| `data` | `object` | Yes | Event-specific data (varies by `eventType`) |

---

## Event Types

### `tool.call.completed`

Fired after every MCP tool call completes (success or failure).

**Target**: Tool webhook URL (`webhook-tool-url`)

```json
{
  "schemaVersion": "1.0",
  "eventType": "tool.call.completed",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "toolName": "add",
    "requestSizeBytes": 42,
    "responseSizeBytes": 18,
    "status": "success",
    "durationMs": 150,
    "errorDetails": null
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `toolName` | `string` | Yes | Name of the invoked tool |
| `requestSizeBytes` | `integer` | Yes | Byte size of serialized request arguments |
| `responseSizeBytes` | `integer` | Yes | Byte size of serialized response (0 on failure with no response) |
| `status` | `string` | Yes | `"success"` or `"failure"` |
| `durationMs` | `integer` | Yes | Full round-trip duration in milliseconds |
| `errorDetails` | `string` or `null` | No | Error message on failure; `null`/omitted on success |

---

### `prompt.retrieved`

Fired when a prompt is retrieved by an MCP client.

**Target**: Prompt webhook URL (`webhook-prompt-url`)

```json
{
  "schemaVersion": "1.0",
  "eventType": "prompt.retrieved",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "promptName": "code-review",
    "responseSizeBytes": 1024
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `promptName` | `string` | Yes | Name/ID of the retrieved prompt |
| `responseSizeBytes` | `integer` | Yes | Byte size of serialized prompt content |

---

### `resource.accessed`

Fired when a resource is accessed by an MCP client.

**Target**: Resource webhook URL (`webhook-resource-url`)

```json
{
  "schemaVersion": "1.0",
  "eventType": "resource.accessed",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "resourceUri": "file:///project/README.md",
    "responseSizeBytes": 4096
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `resourceUri` | `string` | Yes | Resource URI or matched URI template identifier |
| `responseSizeBytes` | `integer` | Yes | Byte size of serialized resource content |

---

### `connection.ai-agent.connected`

Fired when an AI agent (MCP client) completes the MCP initialize handshake.

**Target**: Connection webhook URL (`webhook-connection-url`)

```json
{
  "schemaVersion": "1.0",
  "eventType": "connection.ai-agent.connected",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "eventType": "connected",
    "clientType": "ai-agent",
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "clientName": "Claude Desktop",
    "clientVersion": "1.2.3",
    "metadata": {
      "title": "Claude",
      "description": "Anthropic AI Assistant"
    }
  }
}
```

### `connection.ai-agent.disconnected`

Fired when an AI agent (MCP client) session ends.

```json
{
  "schemaVersion": "1.0",
  "eventType": "connection.ai-agent.disconnected",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "eventType": "disconnected",
    "clientType": "ai-agent",
    "sessionId": "550e8400-e29b-41d4-a716-446655440000"
  }
}
```

### `connection.plugin.connected`

Fired when a McpPlugin (.NET client) connects via SignalR.

```json
{
  "schemaVersion": "1.0",
  "eventType": "connection.plugin.connected",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "eventType": "connected",
    "clientType": "plugin",
    "sessionId": "abc123-signalr-connection-id",
    "clientName": "MyUnityApp",
    "clientVersion": "2.0.0"
  }
}
```

### `connection.plugin.disconnected`

Fired when a McpPlugin (.NET client) disconnects from SignalR.

```json
{
  "schemaVersion": "1.0",
  "eventType": "connection.plugin.disconnected",
  "timestamp": "2026-03-01T12:34:56.789+00:00",
  "data": {
    "eventType": "disconnected",
    "clientType": "plugin",
    "sessionId": "abc123-signalr-connection-id"
  }
}
```

**Connection event fields**:

| Field | Type | Required | Description |
|---|---|---|---|
| `eventType` | `string` | Yes | `"connected"` or `"disconnected"` |
| `clientType` | `string` | Yes | `"ai-agent"` or `"plugin"` |
| `sessionId` | `string` | Yes | MCP session UUID or SignalR connection ID |
| `clientName` | `string` | No | Client name from handshake; omitted if unavailable |
| `clientVersion` | `string` | No | Client version from handshake; omitted if unavailable |
| `metadata` | `object` | No | Additional key-value pairs; omitted if empty |

---

## Error Handling (Consumer Guidance)

- **2xx responses**: Delivery considered successful. No specific response body expected.
- **4xx/5xx responses**: Logged server-side as delivery failure. No retry.
- **Network errors**: Logged server-side. No retry.
- **Timeout**: Cancelled after configured timeout. Logged server-side. No retry.

Consumers should implement idempotent ingestion if guaranteed delivery is required (use external durable queue in front of the webhook endpoint).

---

## Configuration Reference

| CLI Argument | Environment Variable | Default | Description |
|---|---|---|---|
| `webhook-tool-url` | `MCP_PLUGIN_WEBHOOK_TOOL_URL` | *(none)* | Tool event webhook URL |
| `webhook-prompt-url` | `MCP_PLUGIN_WEBHOOK_PROMPT_URL` | *(none)* | Prompt event webhook URL |
| `webhook-resource-url` | `MCP_PLUGIN_WEBHOOK_RESOURCE_URL` | *(none)* | Resource event webhook URL |
| `webhook-connection-url` | `MCP_PLUGIN_WEBHOOK_CONNECTION_URL` | *(none)* | Connection event webhook URL |
| `webhook-token` | `MCP_PLUGIN_WEBHOOK_TOKEN` | *(none)* | Security token value |
| `webhook-header` | `MCP_PLUGIN_WEBHOOK_HEADER` | `X-Webhook-Token` | Header name for security token |
| `webhook-timeout` | `MCP_PLUGIN_WEBHOOK_TIMEOUT` | `10000` | HTTP timeout in milliseconds |
