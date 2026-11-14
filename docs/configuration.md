# Configuration

Settings of the bridge (`McpPlugin.Server`) and of the in-app plugin (`McpPlugin`).

- [Server](#server-mcppluginserver): launch arguments and environment variables
- [Editor and runtime connections](#editor-and-runtime-connections)
- [Analytics webhooks](#analytics-webhooks)
- [Authorization webhook](#authorization-webhook)
- [Plugin](#plugin-mcpplugin): `ConnectionConfig`
- [Docker](#docker)

Transport and auth modes in depth: [architecture-transport-auth.md](architecture-transport-auth.md).

## Server (`McpPlugin.Server`)

`DataArguments` reads the environment first, then the command line (`key=value`), so a launch argument wins.

| Argument / env var | Default | What it does |
| :--- | :--- | :--- |
| `port`<br>`MCP_PLUGIN_PORT` | `8080` | Port for the SignalR hub and HTTP endpoints |
| `client-transport`<br>`MCP_PLUGIN_CLIENT_TRANSPORT` | `streamableHttp` | `stdio` or `streamableHttp` |
| `bind`<br>`MCP_BIND` | loopback | `loopback`, `any` (`0.0.0.0`) or an IP |
| `plugin-timeout`<br>`MCP_PLUGIN_CLIENT_TIMEOUT` | `10000` | One attempt of a list / get / read call (ms) |
| `tool-timeout`<br>`MCP_PLUGIN_TOOL_TIMEOUT` | `300000` | Deadline for a whole tool call (ms) |
| `plugin-reconnect-wait-seconds`<br>`MCP_PLUGIN_RECONNECT_WAIT_SECONDS` | `60` | How long a request waits for a reconnecting plugin |
| `plugin-reconnect-grace-seconds`<br>`MCP_PLUGIN_RECONNECT_GRACE_SECONDS` | `300` | How long a gone plugin's catalog is remembered |
| `idle-timeout-seconds`<br>`MCP_PLUGIN_IDLE_TIMEOUT_SECONDS` | `600` | Idle MCP session eviction (HTTP only) |
| `max-idle-session-count`<br>`MCP_PLUGIN_MAX_IDLE_SESSION_COUNT` | `1000` | Ceiling on idle MCP sessions (HTTP only) |
| `auth`<br>`MCP_AUTH` | `none` | `none`, `token` or `oauth` |
| `token`<br>`MCP_PLUGIN_TOKEN` |  | Shared secret for `auth=token` |
| `auth-issuer`<br>`MCP_AUTH_ISSUER` |  | Authorization server URL for `auth=oauth` |
| `public-url`<br>`MCP_PUBLIC_URL` |  | Public URL (token audience) for `auth=oauth` |

Details:

- **`plugin-timeout`** covers listing tools, prompts or resources, getting a prompt and reading a resource. These
  calls are retried up to 10 times with a 1 s delay. Tool calls do not use it. The four catalog endpoints
  (`tools/list`, `resources/list`, `resources/templates/list`, `prompts/list`) are capped at a fixed 15 s and answer
  an empty catalog instead of failing.
- **`tool-timeout`** covers MCP and REST alike: waiting for a reconnecting plugin, the call itself and a deferred
  (`Processing`) completion. When it passes the call fails with `[tool_timeout]` (HTTP 504). When unset: the
  default, or `plugin-timeout` if that is larger.
- **`plugin-reconnect-wait-seconds`** is also the window in which a call refused as not executed is sent again.
- **`plugin-reconnect-grace-seconds`** lets an agent listing during a reload still see the catalog. `0` disables it.
- **`idle-timeout-seconds`** never evicts a session with an in-flight request or an open SSE stream;
  **`max-idle-session-count`** prunes the least recently active idle sessions first.
- **`auth`**: `none` is anonymous (local dev, CI), `token` is one shared secret for a loopback single-project
  server, `oauth` is an OAuth 2.1 resource server. `token`, `auth-issuer` and `public-url` are required by their
  modes. The legacy `authorization` / `MCP_AUTHORIZATION` names still parse, and the deprecated value `required`
  is treated as `token`.

Tool calls in more detail: a call is sent once, and sent again only when the plugin answered
`ResponseCallTool.NotExecuted`. If the connection drops after sending, the server waits up to
`plugin-reconnect-wait-seconds` and answers `[plugin_reconnected]` or `[plugin_disconnected]` (HTTP 502: the tool
may have run). Arguments are checked against the tool's input schema first (`[invalid_params]`, HTTP 400), and a
lone UTF-16 surrogate is refused (`[invalid_utf16]`). See [tool-errors.md](tool-errors.md).

Endpoints the server maps:

| Path | For |
| :--- | :--- |
| `/hub/mcp-server` | SignalR hub the plugins connect to |
| `/` and `/mcp` | MCP over streamable HTTP (when `client-transport=streamableHttp`) |
| `GET /api/tools`, `POST /api/tools/{name}` | Direct REST tool calls (open in `auth=none`, authorized in the other modes) |

## Editor and runtime connections

In the single-project modes (`none`, `token`) the server keeps one **editor** and one **runtime** (game) plugin
connection side by side. A new connection replaces only an older connection of the same role, and agents are
routed to the editor; a runtime serves only when no editor is connected and none left within
`plugin-reconnect-wait-seconds` (a standalone game build).

- A plugin declares its role with `ConnectionConfig.ClientRole` (`Editor` by default, `Runtime` for a game
  build), sent as the hub query parameter `client_role=editor|runtime` (or the `X-Client-Role` header). A plugin
  that sends neither is a runtime when its handshake `Environment` ends in `(runtime)`, otherwise an editor.
- Game-only tools are marked `[AiToolType(Target = ToolTarget.Runtime)]` (or per tool
  `[AiTool("name", Target = ToolTarget.Runtime)]`). An editor does not register them; the server routes their
  calls to the runtime and lists them while the editor is connected.
- The role is read once, at `Build()`, so set it before building the plugin.
- Connections from plugins before 8.14.0 keep the older newest-wins replacement: the roles apply only when both
  connections are 8.14+ plugins. An old plugin's connection replaces the previous one at its version handshake
  (milliseconds after connecting), not at connect. The handshake's `mcpPluginVersion` field is optional: a
  server before 8.14.0 ignores it.

## Analytics webhooks

Fire-and-forget HTTP POST notifications. Each event category has its own URL; with no URL configured the
subsystem is inactive.

| Argument / env var | Default | What it does |
| :--- | :--- | :--- |
| `webhook-tool-url`<br>`MCP_PLUGIN_WEBHOOK_TOOL_URL` |  | Receives tool call events |
| `webhook-prompt-url`<br>`MCP_PLUGIN_WEBHOOK_PROMPT_URL` |  | Receives prompt retrieval events |
| `webhook-resource-url`<br>`MCP_PLUGIN_WEBHOOK_RESOURCE_URL` |  | Receives resource access events |
| `webhook-connection-url`<br>`MCP_PLUGIN_WEBHOOK_CONNECTION_URL` |  | Receives connect and disconnect events |
| `webhook-token`<br>`MCP_PLUGIN_WEBHOOK_TOKEN` |  | Token sent in each webhook request |
| `webhook-header`<br>`MCP_PLUGIN_WEBHOOK_HEADER` | `X-Webhook-Token` | Header name for that token |
| `webhook-timeout`<br>`MCP_PLUGIN_WEBHOOK_TIMEOUT` | `10000` | Delivery timeout (ms) |

Event types: `tool.call.completed`, `prompt.retrieved`, `resource.accessed`, `connection.ai-agent.connected`,
`connection.ai-agent.disconnected`, `connection.plugin.connected`, `connection.plugin.disconnected`.

Every event uses one envelope:

```json
{
  "schemaVersion": "1.0",
  "eventType": "tool.call.completed",
  "timestamp": "2026-03-01T12:34:56.789Z",
  "data": {
    "toolName": "add",
    "requestSizeBytes": 42,
    "responseSizeBytes": 18,
    "status": "success",
    "durationMs": 150
  }
}
```

Delivery failures are logged and never block MCP responses. Use HTTPS endpoints so the token is not sent in
clear text. Full payload contracts: [specs/001-analytics-webhooks/contracts/webhook-api.md](../specs/001-analytics-webhooks/contracts/webhook-api.md).

## Authorization webhook

A synchronous webhook that allows or denies each connection, from AI agents (HTTP) and from plugins (SignalR).

| Argument / env var | Default | What it does |
| :--- | :--- | :--- |
| `webhook-authorization-url`<br>`MCP_PLUGIN_WEBHOOK_AUTHORIZATION_URL` |  | Endpoint that allows or denies connections; unset allows all |
| `webhook-authorization-fail-open`<br>`MCP_PLUGIN_WEBHOOK_AUTHORIZATION_FAIL_OPEN` | `false` | `true` allows a connection when the webhook times out or errors |

The server POSTs an event of type `authorization.ai-agent` or `authorization.plugin` with the connection's
details (bearer token, remote address and user agent for agents; client name and version for plugins). When
`webhook-token` is set, the body also carries `hmacSignature`: an HMAC-SHA256 of the body computed with the token.
Your endpoint answers:

```json
{ "allowed": false, "reason": "IP not in allowlist" }
```

A 2xx answer with `allowed: true` lets the connection proceed; `allowed: false`, a non-2xx answer, a timeout or
a parse error denies it (unless fail-open). The server waits up to `webhook-timeout`, so keep the endpoint fast.

## Plugin (`McpPlugin`)

Set these with `McpPluginBuilder.WithConfig(config => ...)` before `Build()`.

| Property | Default | What it does |
| :--- | :--- | :--- |
| `Host` | `http://localhost:8080` | URL of the server |
| `TimeoutMs` | `10000` | Operation timeout (ms) |
| `KeepConnected` | `true` | Reconnect when the connection is lost |
| `ClientRole` | `Editor` | `Editor` or `Runtime` (see above) |
| `CredentialProvider` | `null` | Returns the bearer token on every (re)connect |
| `GenerateSkillFiles` | `true` | Write a `SKILL.md` file per registered tool |
| `SkillsPath` | `SKILLS` | Folder for the generated skill files |
| `ProjectRootPath` | `null` | Absolute folder a relative `SkillsPath` is resolved against |
| `SkillMetadata` | empty | Metadata written into every skill file |
| `MaxConsecutiveConnectionFailures` | `0` | Stop reconnecting after this many failures (`0`: never) |

- `WithConfigFromArgsOrEnv(args)` fills `Host` from `mcp-server-endpoint` / `MCP_SERVER_ENDPOINT` and `TimeoutMs`
  from `mcp-server-timeout` / `MCP_SERVER_TIMEOUT`.
- `ConnectionConfig.GetSkillsFolderFromArgsOrEnv(args)` reads `mcp-skills-folder` / `MCP_SKILLS_FOLDER`.
- `CredentialProvider` is a `Func<Task<string?>>`; `null` connects anonymously.
- Without `ProjectRootPath`, skill generation for a relative `SkillsPath` fails.
- `SkillMetadata` entries override the default `generated-by` value.

## Docker

`Dockerfile.local` builds the demo server (`DemoWebApp`) from the repository root:

```bash
docker build -f Dockerfile.local -t mcp-server:local-dev .
```

Inside a container, pass `bind=any` so the server listens beyond loopback.
