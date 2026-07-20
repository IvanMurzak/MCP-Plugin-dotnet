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
> `{instanceId, engine, projectName, projectPathHash, projectPathHashLegacy, machineName}` rides as non-secret hub query
> parameters (the token never appears in the query). It also fixes the direct-tool REST endpoints
> (`/api/tools`, `/api/system-tools`): they now **require authorization in `oauth` mode** (they
> previously gated on the deleted `required` mode and were unintentionally never gated), and stay
> open in `none` mode.

> **Offline token mode (mcp-authorize g6).** A third auth mode, **`token`**, is the OFFLINE
> counterpart of `oauth`: a loopback single-project server gates BOTH the SignalR plugin connection
> and the streamableHttp MCP endpoint on a single static bearer secret (`--token` /
> `MCP_PLUGIN_TOKEN`), validated with a **constant-time** compare (`LocalTokenMcpStrategy`). No
> authorization server, JWT, or egress is required. This is NOT a revival of the b5 multi-tenant
> token-equality pairing — it is one plugin + one secret. The legacy `authorization=required` value
> is **retained as a deprecated alias** onto this strategy (so an un-migrated config runs token-gated
> instead of crashing), never a silent downgrade to anonymous. Constant-time compare is a deliberate
> security upgrade over the b5-era `string.Equals(Ordinal)`.

> **Pin v2 + dual-hash (auth-fixes T3, defect B5).** The routing pin/hash derivation
> (`ProjectIdentity`) gained a **v2** normalization that converts `\` to `/` before hashing, so a
> Windows project root reported with backslashes and the same root reported with forward slashes hash
> IDENTICALLY (previously they diverged — B5, a Windows-only routing failure). The transition is
> **seamless** and requires no server protocol change: the plugin sends BOTH the v2 hash
> (`project_path_hash`) AND the v1 hash (`project_path_hash_legacy`) in the handshake, and
> `PluginInstance.MatchesPin` matches a session pin as a prefix of **either** hash — so an OLD
> `.mcp.json` (v1 pin) keeps routing to a NEW plugin, and a NEW config (v2 pin) routes too. The v1
> methods and their golden vectors (`ProjectIdentity.GoldenVectors.json`) are kept untouched for the
> legacy hash; the v2 vectors live alongside in `ProjectIdentity.GoldenVectors.v2.json` (the shared
> cross-language artifact the engine-CLI ports reproduce). Configurators now emit the v2 pin.

> **Written config port precedence (auth-fixes T1, defect A).** The port a configurator writes —
> the stdio `port=` arg and the loopback HTTP `url` alike — is `AgentConfiguratorSettings.PinnedPort`,
> resolved by three levels: **1.** the project marker's `portOverride`, **2.** an explicit port the
> user typed into `Host`, **3.** the deterministic v2 derived port. Level 2 exists because the engine
> binder already binds the typed port (`UnityMcpPluginEditor.Port` returns `uri.Port` whenever `Host`
> parses with an in-range port) for **both** transports; writing a different port there told the agent
> to dial one nothing was listening on. `ResolvedPort` supplies levels 1 and 3 only and is **not** what
> the writers emit. The per-transport difference is at the call site, not in the precedence: the HTTP
> url applies the port only to a `Local` **loopback** authority (a hosted target keeps its authority —
> and therefore any typed port — verbatim), while stdio has no authority to preserve and applies the
> precedence directly, matching the ungated binder.

## Overview

The MCP Plugin Server is configured through **two independent axes**: **Transport** and **Auth**.
Each axis has its own enum, CLI argument, and environment variable. They combine freely — any transport works with any auth option.

```
Transport  ×  Auth
   stdio          none    (offline / local dev / CI — anonymous, single plugin)
   streamableHttp token   (offline shared-secret — loopback single-project, constant-time gate)
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

| Source           | Key                          | Values                    |
|------------------|------------------------------|---------------------------|
| CLI argument     | `auth=<value>`               | `none`, `token`, `oauth`  |
| Environment var  | `MCP_AUTH=<value>`           | `none`, `token`, `oauth`  |

**Default**: `none` (when not set). `auth=oauth` additionally requires `auth-issuer=<AS url>` and
`public-url=<this RS's canonical resource id>`; `auth=token` additionally requires a non-empty
`token=<secret>` / `MCP_PLUGIN_TOKEN`. (The legacy `authorization=` / `MCP_AUTHORIZATION=` argument
names still parse; the deprecated `required` value is aliased onto `token` — see the g6 note above.)

**Optional server-side metadata / fetch-base override** (`auth-metadata-url=<url>` /
`MCP_AUTH_METADATA_URL`): in `oauth` mode the RS normally fetches JWKS, OAuth introspection, and
account enrollment from `auth-issuer`. When this override is set, those **server-side fetches** use
`<url>` as their base instead; the token `iss` claim check and the RFC 9728 PRM
`authorization_servers` (both client-facing) stay on `auth-issuer`. Unset (the default, incl. all of
prod) → behavior is byte-identical to deriving every fetch URL from the issuer. Use it for a
fully-local OAuth deployment where the client resolves the AS at a host address (e.g.
`http://localhost`) that, inside the RS container, would point back at the container itself.

### Credentials — validated, never minted

In `oauth` mode the RS validates presented credentials against the external authorization server
(ai-game.dev) — ES256 JWTs via cached JWKS, and opaque PATs via RFC 7662 introspection. It **never**
mints, self-issues, or equality-pairs tokens; there is no server-side shared secret. See
`03-auth-flows.md` (Flows A–C) for the full credential lifecycle. In `token` mode the RS holds a
single static shared secret (from `--token` / `MCP_PLUGIN_TOKEN`) and constant-time-compares the
presented bearer against it — the offline, loopback-only counterpart of `oauth`; it still never mints
tokens. In `none` mode no credential is required or accepted (the endpoint is anonymous).

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
  ├── AuthOption.none     → NoAuthMcpStrategy
  ├── AuthOption.token    → LocalTokenMcpStrategy
  ├── AuthOption.required → LocalTokenMcpStrategy   (deprecated alias, g6)
  └── AuthOption.oauth    → AccountMcpStrategy
```

`unknown` (or any unrecognised value) throws at startup — fail closed, never a silent downgrade to `none`.

### NoAuthMcpStrategy (`auth=none`)

Designed for a **single trusted plugin** connecting to a single MCP client (e.g. a developer's local machine running one Unity editor). The HTTP endpoint is never token-gated in this mode; the anonymous, single-connection behavior is unchanged. This is the offline / local-dev / CI default and the stdio default.

### LocalTokenMcpStrategy (`auth=token`, mcp-authorize g6)

The **offline shared-secret** plane — the loopback single-project counterpart of `AccountMcpStrategy`. Modeled on `NoAuthMcpStrategy` (single plugin connection, broadcast routing) but gated on one static secret: `Validate` requires a non-empty `--token`; `ConfigureAuthentication` flags the local-token validation path so the streamableHttp endpoint is `RequireAuthorization`-gated (constant-time compare, **no** RFC 9728 resource metadata — there is no AS to discover); `OnPluginConnected` rejects a tokenless or mismatched plugin (constant-time compare) then enforces the single-connection invariant. The secret is compared via `TokenComparison.FixedTimeEquals` (fixed-length SHA-256 digest, no length side channel). A non-loopback `--bind` is **warned but allowed** (LAN cleartext-token caveat, owner ruling). The deprecated `required` value resolves to this same strategy (back-compat).

### AccountMcpStrategy (`auth=oauth`)

The OAuth 2.1 resource-server **account + instance pairing plane**. A presented credential is validated against the authorization server (ES256 JWT via JWKS, or opaque PAT via introspection) and resolves to an ai-game.dev account (`sub`). Routing is strictly **account-scoped**: an agent session resolves to a live plugin instance by `pin(strict) → sticky → single → most-recently-active`, and a session for one account can never route to, be notified about, or observe another account's instances (fail closed). Full spec: the mcp-authorize design (`04-pairing-and-routing.md`).

**Plane-aware audience validation (auth-fixes B11).** `AccessTokenValidator` accepts a `TokenValidationPlane` so the same validator enforces *different* audiences on the two planes, and the two must stay separated:

- **Agent plane** (an AI-agent MCP request, via `TokenAuthenticationHandler`): the token `aud` must be the RS's canonical resource id (`--public-url`, e.g. `https://ai-game.dev/mcp`). A plugin hub-token is rejected here.
- **Plugin plane** (an engine-plugin hub registration, via `McpServerHub.TryRegisterOAuthInstanceAsync`): a strict allow-list — the plugin audience `urn:agd:hub` (exact), **or** the canonical resource id when the token also carries the `mcp:plugin` scope. This is required because plugin hub-tokens are minted with `aud=urn:agd:hub` (a plane marker, not the RS resource id); without the plugin-plane allow-list the connection is accepted but the instance is never registered → the account bucket stays empty → the agent's `tools/list` silently degrades to the 3 native tools. The `mcp:plugin` scope guard keeps an agent token (`aud=`canonical, `scope=mcp:agent`) from ever registering as a plugin instance.

---

## Behavior Comparison

| Property / method                 | `NoAuthMcpStrategy` (`none`)                                   | `LocalTokenMcpStrategy` (`token`)                                                     | `AccountMcpStrategy` (`oauth`)                                                        |
|-----------------------------------|----------------------------------------------------------------|--------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------|
| `AllowMultipleConnections`        | `false`                                                        | `false`                                                                              | `true`                                                                               |
| `Validate`                        | No-op.                                                          | Requires a non-empty `--token`; warns (allows) on non-loopback `--bind`.             | Requires `auth-issuer` + `public-url`.                                                |
| `ConfigureAuthentication`         | `OAuthMode = false` (anonymous endpoint).                      | `LocalTokenMode = true` (constant-time compare vs the static `--token`).             | `OAuthMode = true` (validate JWT/PAT against the AS; the RS never mints tokens).      |
| `OnPluginConnected`               | Registers, then **disconnects all others** (single-connection).| **Rejects** tokenless/mismatched (constant-time); else registers + disconnects others (single-connection). | Registration is driven by `McpServerHub` after it validates the plugin's token + reads its instance metadata (account-scoped registry). |
| `ResolveConnectionId`             | Token lookup, then round-robin fallback.                       | Token lookup, then round-robin fallback (single connection).                        | Account-scoped instance resolution; no fallback across accounts (fail closed).       |
| `ShouldNotifySession` / `ResolveNotificationTarget` | Broadcast (single-plugin invariant).        | Broadcast (single-plugin invariant).                                                | Targets the session's account-resolved instance, else **drops** — never leaks across accounts. |
| `GetClientData` / `GetServerData` | First available session (only one).                           | First available session (only one).                                                 | Scoped to the connection's account; empty when it resolves to no account.            |

---

## Combination Matrix

| Transport        | Auth    | Typical use case                                                                                          |
|------------------|---------|----------------------------------------------------------------------------------------------------------|
| `stdio`          | `none`  | **Local dev, single user, offline.** The MCP client spawns the server as a subprocess. No accounts, no network. First spawn owns the derived per-project port; a later same-project spawn detects the live server and exits with an actionable message (design 03 Flow D). |
| `streamableHttp` | `none`  | **Local HTTP server, single plugin.** Anonymous HTTP transport for local testing.                        |
| `streamableHttp` | `token` | **Offline shared-secret, single plugin.** Loopback-only local server gated on one static `--token`, constant-time compared — no authorization server / egress required. The offline counterpart of `oauth`. |
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
