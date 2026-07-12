# Transport + Auth Architecture

> **Breaking change (mcp-authorize b5).** The legacy `authorization=required` shared-token
> pairing mode — `RequiredAuthMcpStrategy`, the `--token` / `MCP_PLUGIN_TOKEN` shared secret used
> for token-equality pairing, and the in-process DCR / client-credentials mini-AS
> (`/oauth/register`, `/oauth/token`, `ClientRegistrationStore`) — has been **deleted**. The
> resource server never mints, self-issues, or equality-pairs tokens; it only validates them.
> The two supported auth modes are now **`none`** and **`oauth`**. The authoritative design lives
> in the mcp-authorize design (`02-target-architecture.md`) and `03-auth-flows.md`.

> **Follow-up (mcp-authorize b7).** The client/plugin side of the shared-token removal is now
> complete: the engine plugin no longer reads a static `MCP_PLUGIN_TOKEN` / `mcp-plugin-token`
> shared token. `ConnectionConfig.Token` is replaced by a **credential-provider callback** that
> presents an auto-refreshed account JWT (machine-store auto-adopt — the zero-button rule) in the
> `Authorization` header, and the instance-metadata handshake
> `{instanceId, engine, projectName, projectPathHash, machineName}` rides as non-secret hub query
> parameters (the token never appears in the query). It also fixes the direct-tool REST endpoints
> (`/api/tools`, `/api/system-tools`): they now **require authorization in `oauth` mode** (they
> previously gated on the deleted `required` mode and were unintentionally never gated), and stay
> open in `none` mode.

## Overview

The MCP Plugin Server is configured through **two independent axes**: **Transport** and **Auth**.
Each axis has its own enum, CLI argument, and environment variable. They combine freely — any transport works with any auth option.

```
Transport  ×  Auth
   stdio          none    (offline / local dev / CI — anonymous, single plugin)
   streamableHttp oauth   (OAuth 2.1 resource server — account-scoped pairing)
```

All combinations are supported without conditional branching in the server code. The correct behavior is selected automatically at startup via the **Strategy Pattern** and **Transport Layer Pattern**.

---

## Configuration

### Transport — how MCP clients (e.g. Claude Code) connect

| Source           | Key                                   | Values                        |
|------------------|---------------------------------------|-------------------------------|
| CLI argument     | `client-transport=<value>`            | `stdio`, `streamableHttp`     |
| Environment var  | `MCP_PLUGIN_CLIENT_TRANSPORT=<value>` | `stdio`, `streamableHttp`     |

**Default**: `streamableHttp` (when not set)

### Auth — how plugin connections are authenticated and isolated

| Source           | Key                          | Values           |
|------------------|------------------------------|------------------|
| CLI argument     | `auth=<value>`               | `none`, `oauth`  |
| Environment var  | `MCP_AUTH=<value>`           | `none`, `oauth`  |

**Default**: `none` (when not set). `auth=oauth` additionally requires `auth-issuer=<AS url>` and
`public-url=<this RS's canonical resource id>`. (The legacy `authorization=` / `MCP_AUTHORIZATION=`
argument names still parse for the `none` value; the retired `required` value now fails closed with
an explicit startup error.)

### Credentials — validated, never minted

In `oauth` mode the RS validates presented credentials against the external authorization server
(ai-game.dev) — ES256 JWTs via cached JWKS, and opaque PATs via RFC 7662 introspection. It **never**
mints, self-issues, or equality-pairs tokens; there is no server-side shared secret. See
`03-auth-flows.md` (Flows A–C) for the full credential lifecycle. In `none` mode no credential is
required or accepted (the endpoint is anonymous).

### Idle Timeout — streamableHttp session eviction window

Controls `HttpServerTransportOptions.IdleTimeout` for the streamableHttp transport. Idle MCP sessions are evicted from the server's in-memory session tracker after this many seconds without activity; once evicted, the next request from that client either returns 404 or takes the rehydrate path through a registered `ISessionMigrationHandler`.

| Source           | Key                                          | Values            |
|------------------|----------------------------------------------|-------------------|
| CLI argument     | `idle-timeout-seconds=<int>`                 | Positive integer  |
| Environment var  | `MCP_PLUGIN_IDLE_TIMEOUT_SECONDS=<int>`      | Positive integer  |

**Default**: `600` (10 minutes). The MCP SDK's own default is `7200` (2 hours).

**Eviction only targets genuinely-idle sessions.** The SDK protects any session whose `IsActive` flag is set — a session with an in-flight request (e.g. a 10-minute `script-execute`) or with an open server→client SSE stream is **never** evicted by the idle timeout, regardless of how short it is. The idle timeout therefore reaps only disconnected (zombie) sessions, each of which would otherwise pin its grown `SseEventWriter` buffer until disposed. A short window is safe for live clients and keeps the in-memory footprint bounded.

Trade-off:

- **Longer values** reduce 404s and migration-rehydrate cost for slow-reconnecting clients (consumer reconnect latencies routinely exceed 30 s).
- **Shorter values** keep the in-memory session-tracker footprint bounded.
- Test scenarios that intentionally exercise eviction may want a small value (e.g. `5`). Non-positive values are rejected and the default is used.

This setting is ignored when `client-transport=stdio` (the stdio transport has no idle-eviction concept).

### Max Idle Session Count — streamableHttp hard ceiling on retained idle sessions

Controls `HttpServerTransportOptions.MaxIdleSessionCount`. When the number of idle (non-`IsActive`) MCP sessions exceeds this value, the SDK prunes the least-recently-active idle sessions first — disposing them and returning their per-session `SseEventWriter` buffers to the array pool — even before `idle-timeout-seconds` elapses. This is the hard upper bound that keeps worst-case per-session buffer memory bounded under connection churn. Active sessions (in-flight request or open SSE stream) are never counted toward this ceiling and are never pruned by it.

| Source           | Key                                          | Values            |
|------------------|----------------------------------------------|-------------------|
| CLI argument     | `max-idle-session-count=<int>`               | Positive integer  |
| Environment var  | `MCP_PLUGIN_MAX_IDLE_SESSION_COUNT=<int>`    | Positive integer  |

**Default**: `1000`. The MCP SDK's own default is `10000`. Non-positive values are rejected and the default is used. This setting is ignored when `client-transport=stdio`.

> **Why both knobs matter (issue #119).** Production accumulated thousands of zombie `StreamableHttpSession` entries — each pinning a `PooledByteBufferWriter` grown to the largest SSE event it ever served (up to tens of MiB) — leaking multiple GB. The fix is purely lifecycle/eviction tuning: a documented 10-minute idle window reaps disconnected sessions promptly, and an explicit `MaxIdleSessionCount` caps the retained idle set an order of magnitude below the SDK default. Neither change touches the wire protocol, image/screenshot transfer, or auth semantics.

---

## Connection Strategy

The `IMcpConnectionStrategy` interface is the central coordinator for all auth-dependent behavior. A concrete strategy is resolved once at startup by `McpStrategyFactory` based on the configured `AuthOption` and injected as a singleton throughout the server.

```
McpStrategyFactory
  ├── AuthOption.none  → NoAuthMcpStrategy
  └── AuthOption.oauth → AccountMcpStrategy
```

Any other value (including the retired `required`) throws at startup — fail closed, never a silent downgrade to `none`.

### NoAuthMcpStrategy (`auth=none`)

Designed for a **single trusted plugin** connecting to a single MCP client (e.g. a developer's local machine running one Unity editor). The HTTP endpoint is never token-gated in this mode; the anonymous, single-connection behavior is unchanged. This is the offline / local-dev / CI default and the stdio default.

### AccountMcpStrategy (`auth=oauth`)

The OAuth 2.1 resource-server **account + instance pairing plane**. A presented credential is validated against the authorization server (ES256 JWT via JWKS, or opaque PAT via introspection) and resolves to an ai-game.dev account (`sub`). Routing is strictly **account-scoped**: an agent session resolves to a live plugin instance by `pin(strict) → sticky → single → most-recently-active`, and a session for one account can never route to, be notified about, or observe another account's instances (fail closed). Full spec: the mcp-authorize design (`04-pairing-and-routing.md`).

---

## Behavior Comparison

| Property / method                 | `NoAuthMcpStrategy` (`none`)                                   | `AccountMcpStrategy` (`oauth`)                                                        |
|-----------------------------------|----------------------------------------------------------------|--------------------------------------------------------------------------------------|
| `AllowMultipleConnections`        | `false`                                                        | `true`                                                                               |
| `Validate`                        | No-op.                                                          | Requires `auth-issuer` + `public-url`.                                                |
| `ConfigureAuthentication`         | `OAuthMode = false` (anonymous endpoint).                      | `OAuthMode = true` (validate JWT/PAT against the AS; the RS never mints tokens).      |
| `OnPluginConnected`               | Registers, then **disconnects all others** (single-connection).| Registration is driven by `McpServerHub` after it validates the plugin's token + reads its instance metadata (account-scoped registry). |
| `ResolveConnectionId`             | Token lookup, then round-robin fallback.                       | Account-scoped instance resolution; no fallback across accounts (fail closed).       |
| `ShouldNotifySession` / `ResolveNotificationTarget` | Broadcast (single-plugin invariant).        | Targets the session's account-resolved instance, else **drops** — never leaks across accounts. |
| `GetClientData` / `GetServerData` | First available session (only one).                           | Scoped to the connection's account; empty when it resolves to no account.            |

---

## Combination Matrix

| Transport        | Auth    | Typical use case                                                                                          |
|------------------|---------|----------------------------------------------------------------------------------------------------------|
| `stdio`          | `none`  | **Local dev, single user, offline.** The MCP client spawns the server as a subprocess. No accounts, no network. First spawn owns the derived per-project port; a later same-project spawn detects the live server and exits with an actionable message (design 03 Flow D). |
| `streamableHttp` | `none`  | **Local HTTP server, single plugin.** Anonymous HTTP transport for local testing.                        |
| `streamableHttp` | `oauth` | **Signed-in localhost AND hosted.** Account-scoped pairing: many engine instances across accounts, each isolated to its own account's agent sessions; multiple agent sessions in one project fan in to one server. |

`stdio` + `oauth` is valid but uncommon — a signed-in engine may connect with its JWT; in `none` mode the token is ignored for routing (identity is still logged for diagnostics).

---

## Architecture Flow

```
Startup
  DataArguments.ClientTransport ──► TransportFactory ──► ITransportLayer
                                      (stdio / streamableHttp)

  DataArguments.Authorization ────► McpStrategyFactory ──► IMcpConnectionStrategy
                                      (none / oauth)

Both singletons are registered in DI and flow through the entire server:

  BaseHub           ──uses──► IMcpConnectionStrategy (OnPluginConnected / OnPluginDisconnected)
  McpServerHub      ──uses──► IMcpConnectionStrategy (GetClientData / GetServerData; oauth instance registration)
  McpServerService  ──uses──► IMcpConnectionStrategy (ResolveNotificationTarget)
  RemoteToolRunner  ──uses──► IMcpConnectionStrategy (ResolveConnectionId via ClientUtils.InvokeAsync)
  RemotePromptRunner──uses──► IMcpConnectionStrategy
  RemoteResourceRunner──uses►IMcpConnectionStrategy
```

---

## Example Launch Commands

```bash
# Local development (stdio, no auth) — typical Claude Desktop config
dotnet run client-transport=stdio

# Local HTTP server without auth
dotnet run client-transport=streamableHttp port=8080

# OAuth resource-server mode (localhost or hosted) — validates tokens against ai-game.dev
dotnet run client-transport=streamableHttp port=23471 \
    auth=oauth auth-issuer=https://ai-game.dev public-url=http://localhost:23471
```

In `oauth` mode credentials never travel in config files or launch arguments: MCP clients discover
the authorization server via RFC 9728 protected-resource metadata and run the standard OAuth 2.1
authorization-code + PKCE flow; engine plugins connect over SignalR presenting their device-grant
JWT. See the mcp-authorize design (`03-auth-flows.md`).
