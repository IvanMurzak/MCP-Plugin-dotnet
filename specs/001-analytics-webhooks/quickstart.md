# Quickstart: Analytics Webhooks

**Branch**: `001-analytics-webhooks` | **Date**: 2026-03-01

---

## Prerequisites

- McpPlugin.Server running (DemoWebApp or custom host)
- An HTTP endpoint to receive webhook POST requests (e.g., [webhook.site](https://webhook.site), a local Express/Flask server, or a real analytics service)

---

## 1. Enable Tool Call Webhooks (CLI Arguments)

```bash
cd DemoWebApp && dotnet run \
  port=11111 \
  client-transport=stdio \
  webhook-tool-url=https://your-analytics.example.com/hooks/tools \
  webhook-token=my-secret-token
```

This configures the server to POST a JSON payload to the given URL after every MCP tool call, with `X-Webhook-Token: my-secret-token` in the request header.

---

## 2. Enable All Webhook Categories

```bash
cd DemoWebApp && dotnet run \
  port=11111 \
  client-transport=stdio \
  webhook-tool-url=https://analytics.example.com/hooks/tools \
  webhook-prompt-url=https://analytics.example.com/hooks/prompts \
  webhook-resource-url=https://analytics.example.com/hooks/resources \
  webhook-connection-url=https://analytics.example.com/hooks/connections \
  webhook-token=my-secret-token \
  webhook-header=Authorization \
  webhook-timeout=5000
```

---

## 3. Configure via Environment Variables

```bash
export MCP_PLUGIN_WEBHOOK_TOOL_URL=https://analytics.example.com/hooks/tools
export MCP_PLUGIN_WEBHOOK_CONNECTION_URL=https://analytics.example.com/hooks/connections
export MCP_PLUGIN_WEBHOOK_TOKEN=my-secret-token
export MCP_PLUGIN_WEBHOOK_TIMEOUT=5000

cd DemoWebApp && dotnet run port=11111 client-transport=stdio
```

CLI arguments override environment variables when both are provided.

---

## 4. Verify Webhook Delivery

After starting the server and triggering a tool call, your endpoint receives:

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
    "durationMs": 150
  }
}
```

With headers:
```
Content-Type: application/json; charset=utf-8
X-Webhook-Token: my-secret-token
```

---

## 5. Common Configurations

### Minimal (tool analytics only, no token)

```bash
dotnet run webhook-tool-url=https://hooks.example.com/tools
```

The server logs a warning that no security token is configured.

### Production (all categories, HTTPS, custom header)

```bash
dotnet run \
  webhook-tool-url=https://prod.example.com/mcp/tools \
  webhook-prompt-url=https://prod.example.com/mcp/prompts \
  webhook-resource-url=https://prod.example.com/mcp/resources \
  webhook-connection-url=https://prod.example.com/mcp/connections \
  webhook-token=$WEBHOOK_SECRET \
  webhook-header=X-API-Key \
  webhook-timeout=3000
```

### Development (HTTP, local receiver)

```bash
dotnet run webhook-tool-url=http://localhost:9090/webhook
```

The server logs a warning that the URL uses HTTP (non-TLS).

---

## 6. Disabled by Default

When no `webhook-*-url` arguments are provided, the webhook subsystem is completely inactive — no background services, no HTTP clients, no overhead. The server operates identically to a version without webhook support.
