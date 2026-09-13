/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    public class StreamableHttpTransportLayer : ITransportLayer
    {
        /// <summary>
        /// How long a NEW session waits for the session it displaced to finish disposing before it stops
        /// waiting and proceeds. This is a latency bound on <c>initialize</c>, not a correctness bound:
        /// the displaced session is removed from the SDK's session store synchronously inside
        /// <c>IMcpSessionReaper.Claim</c>, so exceeding this timeout cannot leave a reachable session or
        /// reintroduce the pile-up. The wait exists only so the common case is fully settled — and
        /// therefore deterministic — before the replacement starts serving.
        /// </summary>
        public static readonly TimeSpan DisplacedSessionDisposeTimeout = TimeSpan.FromSeconds(10);

        public Consts.MCP.Server.TransportMethod TransportMethod
            => Consts.MCP.Server.TransportMethod.streamableHttp;

        public IMcpServerBuilder ConfigureTransport(
            IMcpServerBuilder mcpServerBuilder,
            DataArguments dataArguments,
            Logger? logger = null)
        {
            return mcpServerBuilder.WithHttpTransport(options =>
            {
                logger?.Debug("Http transport configuration. IdleTimeout={idleTimeoutSeconds}s, MaxIdleSessionCount={maxIdleSessionCount}",
                    dataArguments.IdleTimeoutSeconds, dataArguments.MaxIdleSessionCount);

                options.Stateless = false;
                options.PerSessionExecutionContext = true;
                options.IdleTimeout = TimeSpan.FromSeconds(dataArguments.IdleTimeoutSeconds);
                // Hard ceiling on retained idle sessions. Without this the SDK default (10,000)
                // lets disconnected (zombie) sessions accumulate, each pinning its grown
                // SseEventWriter buffer (up to tens of MiB), which leaked multi-GB in production.
                // Active sessions (in-flight request / open SSE stream) are never pruned by this.
                options.MaxIdleSessionCount = dataArguments.MaxIdleSessionCount;
                // ConfigureSessionOptions cannot replace RunSessionHandler here because we need
                // full session lifecycle management (StartAsync/StopAsync for McpServerService).
                // Suppressing MCPEXP002 as RunSessionHandler is the only mechanism that provides
                // access to session lifetime events (before session starts and after it ends).
#pragma warning disable MCPEXP002
                options.RunSessionHandler = async (context, server, cancellationToken) =>
                {
                    logger?.Debug("-------------------------------------------------\nRunning session handler for HTTP transport. Session ID: {sessionId}",
                        server.SessionId);

                    var mcpClientSessionId = server.SessionId ?? throw new InvalidOperationException("MCP Server session ID is not available.");

                    // Do NOT link context.RequestAborted here — it fires when the initial POST /
                    // (initialize) response is sent, which would kill the entire session before
                    // any subsequent requests (tools/list, SSE GET, etc.) can be served.
                    // The MCP SDK's cancellationToken already manages the session lifetime.
                    var linkedToken = cancellationToken;

                    // Extract Bearer token from the HTTP request for token-based routing
                    var authHeader = context.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        McpSessionTokenContext.CurrentToken = authHeader.Substring("Bearer ".Length).Trim();

                    // Publish the MCP session id on the SESSION's ambient context, exactly like the
                    // bearer token above. This is the only place that can: PerSessionExecutionContext
                    // is true, so every request handler of this session runs on the ExecutionContext
                    // captured here, NOT on the ExecutionContext of the request that carried it. The
                    // per-request AsyncLocal write in McpSessionTokenMiddleware therefore never reaches
                    // a handler — and it could not anyway, because this handler runs during
                    // `initialize`, which per the MCP spec carries no Mcp-Session-Id header (the id is
                    // MINTED in that response). Without this, SelectionToolContext.FromCurrent() always
                    // saw SessionId == null and select_engine_instance failed 100% of the time.
                    McpSessionTokenContext.CurrentSessionId = mcpClientSessionId;

                    // ── Client opt-in flags are SESSION-scoped, decided here (a5 ruling) ───────────────
                    // Both `X-McpPlugin-Skill-Meta` and `X-McpPlugin-Internal-Client` are properties of
                    // the SESSION, read from the `initialize` request and fixed for the session's whole
                    // life. A later request's header is ignored — see the ruling and the rejected
                    // per-request alternative on McpSessionTokenContext.IsSkillMetaClient.
                    //
                    // Publishing them HERE rather than relying on McpSessionTokenMiddleware is the whole
                    // point. The middleware also writes them, and on `initialize` its write lands inside
                    // the ExecutionContext captured below, so today the two agree — but only because the
                    // middleware happens to sit upstream of the capture. That is an accident of pipeline
                    // ordering, not a decision: relocate the middleware, or add any stage that captures
                    // the context earlier, and the session's opt-in silently becomes "whatever was left
                    // in the ambient slot" with nothing to notice. Reading the header from the request
                    // that ESTABLISHES the session makes the semantics true by construction.
                    //
                    // Written unconditionally, `false` included: the value must be the initialize
                    // request's answer, not "the initialize request's answer, or else whatever was
                    // already there". The middleware's own write stays conditional for the opposite and
                    // equally deliberate reason (see the comment at its call site).
                    McpSessionTokenContext.IsSkillMetaClient =
                        ClientOptInHeaders.ReadSkillMetaClient(context.Request.Headers) ?? false;
                    McpSessionTokenContext.IsTrustedInternalClient =
                        ClientOptInHeaders.ReadTrustedInternalClient(context.Request.Headers) ?? false;

                    // ── Replace-by-identity (design 07 §2.3, D10.2) ────────────────────────────────────
                    // This is the only place the rule can run. The installation identity rides on the
                    // `initialize` request, and this handler is the one hook that sees BOTH that request's
                    // HttpContext and the freshly minted session id it is about to be keyed under.
                    //
                    // Ordering is deliberate: the claim happens BEFORE `server.RunAsync` below, so a
                    // predecessor is already unreachable by the time this session starts answering. The
                    // claim itself is synchronous; only the predecessor's resource dispose is awaited, and
                    // that wait is bounded because the leak-stopping half already completed inside Claim().
                    //
                    // Absent header ⇒ `identityLease` stays null ⇒ every line below is skipped and the
                    // session behaves exactly as it does today. That is the released-client path.
                    IMcpSessionIdentityLease? identityLease = null;
                    try
                    {
                        var instanceParse = InstanceIdentityHeader.TryRead(context.Request.Headers, out var instanceId);
                        if (instanceParse == InstanceIdentityParse.Valid)
                        {
                            // accountSub comes from the VALIDATED credential, never from a client-supplied
                            // field — that is what confines a client-supplied instance id to its own account.
                            var identity = ConnectionIdentity.FromPrincipal(context.User)
                                        ?? McpSessionTokenContext.CurrentIdentity;

                            // The pin is re-parsed from the original request path rather than read from
                            // ambient state, so the key never depends on AsyncLocal flow. Malformed pins
                            // were already rejected upstream by McpSessionTokenMiddleware.
                            McpSessionTokenMiddleware.TryExtractProjectPin(context.Request.Path.Value, out var projectPin);

                            var reaper = server.Services?.GetService<IMcpSessionReaper>();
                            identityLease = reaper?.Claim(identity?.AccountId, instanceId, projectPin, mcpClientSessionId);

                            var predecessor = identityLease?.DisplacedPredecessor;
                            if (predecessor != null && !predecessor.DisposeCompletion.IsCompleted)
                            {
                                // Bounded: the predecessor's dispose awaits ITS RunSessionHandler, which in
                                // turn awaits a SignalR disconnect notification. Blocking `initialize`
                                // indefinitely behind another session's teardown would turn a reconnect into
                                // a hang. Timing out here is safe — the predecessor is already evicted from
                                // the SDK's session store — so it is logged and stepped over, not retried.
                                //
                                // The timer is cancelled on the happy path rather than left to expire: a
                                // replace normally settles in milliseconds, and an abandoned Task.Delay would
                                // hold a live timer for the full timeout after every single reconnect.
                                using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken))
                                {
                                    var completed = await Task.WhenAny(
                                        predecessor.DisposeCompletion,
                                        Task.Delay(DisplacedSessionDisposeTimeout, waitCts.Token)).ConfigureAwait(false);
                                    waitCts.Cancel();
                                    if (completed != predecessor.DisposeCompletion)
                                    {
                                        logger?.Warn("Displaced MCP session did not finish disposing within {timeout}; " +
                                            "it is already evicted from the SDK session store, continuing. Session ID: {sessionId}",
                                            DisplacedSessionDisposeTimeout, mcpClientSessionId);
                                    }
                                }
                            }
                        }
                        else if (instanceParse == InstanceIdentityParse.Malformed)
                        {
                            // Defence in depth. McpSessionTokenMiddleware rejects a malformed value with
                            // 400 before routing, so this branch should be unreachable over HTTP; if it is
                            // ever reached, the session is still established (never rejected here, where
                            // rejecting would mean failing an already-minted session) but claims no slot.
                            logger?.Warn("Malformed {header} reached the session handler; no identity slot claimed. Session ID: {sessionId}",
                                InstanceIdentityHeader.HeaderName, mcpClientSessionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        // The replace rule must never be able to prevent a session from starting. A client
                        // that cannot connect is strictly worse than one whose predecessor lingers until the
                        // idle timeout, so this degrades to today's behaviour instead of failing the session.
                        logger?.Error(ex, $"Replace-by-identity failed; continuing without it. Session ID: {mcpClientSessionId}.");
                    }

                    try
                    {
                        var services = server.Services ?? throw new InvalidOperationException("MCP Server services are not available.");
                        var service = new McpServerService(
                            services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<McpServerService>>(),
                            services.GetRequiredService<Common.Version>(),
                            dataArguments,
                            services.GetRequiredService<HubEventToolsChange>(),
                            services.GetRequiredService<HubEventPromptsChange>(),
                            services.GetRequiredService<HubEventResourcesChange>(),
                            services.GetRequiredService<IHubContext<McpServerHub, IClientMcpRpc>>(),
                            services.GetRequiredService<IMcpSessionTracker>(),
                            services.GetRequiredService<IMcpConnectionStrategy>(),
                            services.GetRequiredService<Webhooks.IWebhookEventCollector>(),
                            mcpServer: server,
                            mcpSession: null
                        );

                        try
                        {
                            await service.StartAsync(linkedToken);
                            await server.RunAsync(linkedToken);
                            logger?.Debug("MCP Server completed for HTTP transport. Session ID: {sessionId}",
                                mcpClientSessionId);
                        }
                        finally
                        {
                            // Use CancellationToken.None to ensure cleanup completes
                            // even if the session's cancellation token is already cancelled
                            await service.StopAsync(CancellationToken.None);
                        }
                    }
                    catch (OperationCanceledException) when (identityLease?.Displaced == true)
                    {
                        // Displaced by a newer connection carrying the same installation identity. This is
                        // the rule working, so it must not be logged as a fault: an orderly replace that
                        // reports itself as an error trains operators to ignore the log, and a reconnect
                        // loop would fill it. The tracker row was already removed by StopAsync above.
                        logger?.Debug("MCP session displaced by replace-by-identity; terminated cleanly. Session ID: {sessionId}",
                            mcpClientSessionId);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The SDK cancelled the session's own token: a client DELETE, an idle-timeout
                        // eviction, or host shutdown. Definitionally an orderly end of session, not an error.
                        logger?.Debug("MCP session cancelled by the transport; terminated cleanly. Session ID: {sessionId}",
                            mcpClientSessionId);
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex, $"Error occurred while processing HTTP transport session. Session ID: {mcpClientSessionId}.");
                    }
                    finally
                    {
                        // Release the identity slot LAST, and only if this session still holds it: a
                        // displaced session unwinds after its successor installed itself, so an
                        // unconditional release here would delete the successor's slot and strand a live
                        // session that no later connection could ever replace. McpSessionReaper.Release
                        // does that compare-and-remove atomically.
                        identityLease?.Dispose();
                        logger?.Debug($"-------------------------------------------------\nSession handler for HTTP transport completed. Session ID: {mcpClientSessionId}\n------------------------");
                    }
                };
#pragma warning restore MCPEXP002
            });
        }

        public void ConfigureServices(IServiceCollection services, DataArguments dataArguments)
        {
            // HTTP transport does not register a hosted service
        }

        public void ConfigureApp(WebApplication app, DataArguments dataArguments)
        {
            var logger = LogManager.GetCurrentClassLogger();
            var mode = dataArguments.Authorization;

            logger.Debug("Configuring HTTP transport endpoints. Auth={mode}", mode);

            if (mode == Consts.MCP.Server.AuthOption.oauth)
            {
                // OAuth resource-server mode (mcp-authorize b2): serve ONLY Protected Resource
                // Metadata (RFC 9728) pointing at the EXTERNAL authorization server. The RS never
                // mints tokens, so no /oauth/register, /oauth/token, or AS-metadata are served here.
                var oauthConfig = app.Services.GetService<Auth.OAuth.OAuthResourceServerConfig>();

                // Public, non-secret PRM (RFC 9728). Served at BOTH the bare well-known path AND —
                // when --public-url carries a path (e.g. https://host/mcp) — the path-inserted URL
                // the 401 challenge advertises (OAuthResourceServerConfig.ProtectedResourceMetadataUrl),
                // so a spec-compliant client following the challenge does not 404 on a path-bearing
                // resource. Shared handler avoids duplicating the body across both routes.
                RequestDelegate servePrm = async ctx =>
                {
                    if (oauthConfig == null)
                    {
                        await Results.StatusCode(500).ExecuteAsync(ctx);
                        return;
                    }
                    // CORS-open so browser-based MCP clients can read it.
                    ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    var document = Auth.OAuth.OAuthProtectedResourceMetadata.Build(oauthConfig);
                    await Results.Json(document).ExecuteAsync(ctx);
                };

                const string bareMetadataPath = "/.well-known/oauth-protected-resource";
                app.MapGet(bareMetadataPath, servePrm).AllowAnonymous();

                // Path-bearing resource → also serve at the challenge-advertised path-inserted URL.
                if (oauthConfig != null
                    && Uri.TryCreate(oauthConfig.ProtectedResourceMetadataUrl(), UriKind.Absolute, out var prmUri)
                    && !string.Equals(prmUri.AbsolutePath, bareMetadataPath, StringComparison.Ordinal))
                {
                    app.MapGet(prmUri.AbsolutePath, servePrm).AllowAnonymous();
                }

                app.MapMcp("/").RequireAuthorization();
                app.MapMcp("/mcp").RequireAuthorization();
                MapPinnedMcp(app, requireAuthorization: true);
            }
            else if (mode == Consts.MCP.Server.AuthOption.token
                || mode == Consts.MCP.Server.AuthOption.required)
            {
                // Offline token mode (mcp-authorize g6): gate the MCP endpoint with RequireAuthorization
                // exactly like oauth, but WITHOUT the RFC 9728 Protected-Resource-Metadata routes — there
                // is no authorization server to discover; the credential is the local static --token,
                // validated by TokenAuthenticationHandler's constant-time compare. Without this gate the
                // token would be validated but never ENFORCED (unauthenticated requests would still reach
                // the endpoint). `required` is aliased onto the same token strategy (back-compat).
                app.MapMcp("/").RequireAuthorization();
                app.MapMcp("/mcp").RequireAuthorization();
                MapPinnedMcp(app, requireAuthorization: true);
            }
            else
            {
                // none mode (offline / local dev / CI): anonymous MCP endpoint, no auth gate.
                // The legacy `required` mini-AS (self-issued DCR / client-credentials tokens via
                // /oauth/register + /oauth/token backed by ClientRegistrationStore) was removed in
                // mcp-authorize b5 — the RS never mints tokens.
                app.MapMcp("/");
                app.MapMcp("/mcp");
                MapPinnedMcp(app, requireAuthorization: false);
            }
        }

        /// <summary>
        /// Mount the MCP streamable-HTTP endpoint at the project-pinned config paths (mcp-authorize g4,
        /// design 04 D14 / 03 Flow F). The b6 configurators write <c>https://host/mcp/p/&lt;pin&gt;</c>;
        /// production nginx strips the <c>/mcp</c> prefix so the server receives <c>/p/&lt;pin&gt;</c>,
        /// while a direct/local client (no nginx) sends <c>/mcp/p/&lt;pin&gt;</c>. Both must route to the
        /// SAME handler as <c>/mcp</c> — before this the endpoint mapped only at <c>/</c> and <c>/mcp</c>,
        /// so a pinned request 404'd before <see cref="Auth.McpSessionTokenMiddleware"/> could capture the
        /// pin or the OAuth 401 challenge could run. <c>{pin}</c> is a route token only so the endpoint
        /// matches; its value is ignored by the MCP handler — the pin is captured from the ORIGINAL
        /// request path by the middleware (route-parameter matching preserves the path, unlike a
        /// pre-routing rewrite, which would strip the pin before capture). Served in BOTH auth modes so
        /// the pinned URL behaves exactly like <c>/mcp</c> (401 in oauth, anonymous session in none).
        ///
        /// <para>Because <c>{pin}</c> carries no route constraint, a MALFORMED segment still matches these
        /// routes; <see cref="Auth.McpSessionTokenMiddleware"/> rejects it with <c>400 invalid_project_pin</c>
        /// so no MCP session is ever established for it — it is never downgraded to an unpinned session,
        /// which would resolve <c>sticky → single → MRU</c> onto an arbitrary sibling project.</para>
        /// </summary>
        static void MapPinnedMcp(WebApplication app, bool requireAuthorization)
        {
            var pinned = app.MapMcp("/p/{pin}");
            var pinnedWithPrefix = app.MapMcp("/mcp/p/{pin}");
            if (requireAuthorization)
            {
                pinned.RequireAuthorization();
                pinnedWithPrefix.RequireAuthorization();
            }
        }

    }
}
