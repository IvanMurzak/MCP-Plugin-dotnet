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

| Argument | Env var | Description | Default |
| :--- | :--- | :--- | :--- |
| `port` | `MCP_PLUGIN_PORT` | Port for the SignalR hub and the HTTP endpoints. | `8080` |
| `client-transport` | `MCP_PLUGIN_CLIENT_TRANSPORT` | How MCP clients connect: `stdio` (the client launches the server) or `streamableHttp`. | `streamableHttp` |
| `bind` | `MCP_BIND` | Listen address: `loopback`, `any` (`0.0.0.0`) or a specific IP. | loopback |
| `plugin-timeout` | `MCP_PLUGIN_CLIENT_TIMEOUT` | Timeout (ms) for ONE attempt of an idempotent plugin call: listing tools, prompts or resources, getting a prompt, reading a resource. These calls are retried up to 10 times with a 1 s delay. Tool calls do not use it. The four catalog endpoints (`tools/list`, `resources/list`, `resources/templates/list`, `prompts/list`) are capped at a fixed 15 s and answer an empty catalog instead of failing. | `10000` |
| `tool-timeout` | `MCP_PLUGIN_TOOL_TIMEOUT` | One deadline (ms) for a whole tool call, MCP and REST alike: waiting for a reconnecting plugin, the call itself and a deferred (`Processing`) completion. When it passes the call fails with `[tool_timeout]` (HTTP 504). When unset: the default, or `plugin-timeout` if that is larger. | `300000` |
| `plugin-reconnect-wait-seconds` | `MCP_PLUGIN_RECONNECT_WAIT_SECONDS` | The longest a request waits for a reconnecting plugin (an editor reload) before it fails. Also the window in which a call refused as not executed is sent again. | `60` |
| `plugin-reconnect-grace-seconds` | `MCP_PLUGIN_RECONNECT_GRACE_SECONDS` | How long a disconnected plugin's tool, prompt and resource lists are remembered, so an agent listing during a reload still sees them. `0` disables it. | `300` |
| `idle-timeout-seconds` | `MCP_PLUGIN_IDLE_TIMEOUT_SECONDS` | `streamableHttp` only: idle time before an MCP session is evicted. Sessions with an in-flight request or an open SSE stream are never evicted. | `600` |
| `max-idle-session-count` | `MCP_PLUGIN_MAX_IDLE_SESSION_COUNT` | `streamableHttp` only: ceiling on retained idle MCP sessions; the least recently active are pruned first. | `1000` |
| `auth` | `MCP_AUTH` | `none` (anonymous: local dev, CI), `token` (one shared secret, loopback single project) or `oauth` (OAuth 2.1 resource server). The legacy `authorization` / `MCP_AUTHORIZATION` names still parse, and the deprecated value `required` is treated as `token`. | `none` |
| `token` | `MCP_PLUGIN_TOKEN` | `auth=token` only (required): the shared secret that plugins and MCP clients present as a bearer token. | *(none)* |
| `auth-issuer` | `MCP_AUTH_ISSUER` | `auth=oauth` only (required): the authorization server URL. | *(none)* |
| `public-url` | `MCP_PUBLIC_URL` | `auth=oauth` only (required): this server's public URL, used as the token audience. | *(none)* |

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

## Analytics webhooks

Fire-and-forget HTTP POST notifications. Each event category has its own URL; with no URL configured the
subsystem is inactive.

| Argument | Env var | Description | Default |
| :--- | :--- | :--- | :--- |
| `webhook-tool-url` | `MCP_PLUGIN_WEBHOOK_TOOL_URL` | Receives tool call events. | *(none)* |
| `webhook-prompt-url` | `MCP_PLUGIN_WEBHOOK_PROMPT_URL` | Receives prompt retrieval events. | *(none)* |
| `webhook-resource-url` | `MCP_PLUGIN_WEBHOOK_RESOURCE_URL` | Receives resource access events. | *(none)* |
| `webhook-connection-url` | `MCP_PLUGIN_WEBHOOK_CONNECTION_URL` | Receives connect and disconnect events. | *(none)* |
| `webhook-token` | `MCP_PLUGIN_WEBHOOK_TOKEN` | Security token sent in each webhook request header. | *(none)* |
| `webhook-header` | `MCP_PLUGIN_WEBHOOK_HEADER` | Header name for that token. | `X-Webhook-Token` |
| `webhook-timeout` | `MCP_PLUGIN_WEBHOOK_TIMEOUT` | Delivery timeout (ms). | `10000` |

Event types: `tool.call.completed`, `prompt.retrieved`, `resource.accessed`, `connection.ai-agent.connected`,
`connection.ai-agent.disconnected`, `connection.plugin.connected`, `connection.plugin.disconnected`.

Every event uses one envelope:

```json
{
  "schemaVersion": "1.0",
  "eventType": "tool.call.completed",
  "timestamp": "2026-03-01T12:34:56.789Z",
  "data": { "toolName": "add", "requestSizeBytes": 42, "responseSizeBytes": 18, "status": "success", "durationMs": 150 }
}
```

Delivery failures are logged and never block MCP responses. Use HTTPS endpoints so the token is not sent in
clear text. Full payload contracts: [specs/001-analytics-webhooks/contracts/webhook-api.md](../specs/001-analytics-webhooks/contracts/webhook-api.md).

## Authorization webhook

A synchronous webhook that allows or denies each connection, from AI agents (HTTP) and from plugins (SignalR).

| Argument | Env var | Description | Default |
| :--- | :--- | :--- | :--- |
| `webhook-authorization-url` | `MCP_PLUGIN_WEBHOOK_AUTHORIZATION_URL` | Endpoint that allows or denies connections. Unset: every connection is allowed. | *(none)* |
| `webhook-authorization-fail-open` | `MCP_PLUGIN_WEBHOOK_AUTHORIZATION_FAIL_OPEN` | `true`: allow when the webhook times out or errors. `false`: deny. | `false` |

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
`WithConfigFromArgsOrEnv(args)` fills `Host` and `TimeoutMs` from the arguments and variables marked below.

| Property | Argument / env var | Description | Default |
| :--- | :--- | :--- | :--- |
| `Host` | `mcp-server-endpoint` / `MCP_SERVER_ENDPOINT` | URL of the server. | `http://localhost:8080` |
| `TimeoutMs` | `mcp-server-timeout` / `MCP_SERVER_TIMEOUT` | Operation timeout (ms). | `10000` |
| `KeepConnected` | | Reconnect automatically when the connection is lost. | `true` |
| `ClientRole` | | `Editor` or `Runtime` (see above). | `Editor` |
| `CredentialProvider` | | `Func<Task<string?>>` that returns the bearer token for the hub connection, called on every (re)connect. `null` connects anonymously. | `null` |
| `GenerateSkillFiles` | | Write a `SKILL.md` file for each registered tool. | `true` |
| `SkillsPath` | `mcp-skills-folder` / `MCP_SKILLS_FOLDER` (via `ConnectionConfig.GetSkillsFolderFromArgsOrEnv`) | Folder for the generated skill files. A relative path is resolved against `ProjectRootPath`. | `SKILLS` |
| `ProjectRootPath` | | Absolute folder that a relative `SkillsPath` is anchored to. Without it, skill generation for a relative path fails. | `null` |
| `SkillMetadata` | | Metadata entries written into every generated skill file (they override the default `generated-by`). | empty |
| `MaxConsecutiveConnectionFailures` | | Give up reconnecting after this many failures in a row. `0` retries forever. | `0` |

## Docker

`Dockerfile.local` builds the demo server (`DemoWebApp`) from the repository root:

```bash
docker build -f Dockerfile.local -t mcp-server:local-dev .
```

Inside a container, pass `bind=any` so the server listens beyond loopback.
