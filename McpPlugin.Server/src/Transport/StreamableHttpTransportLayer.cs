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
                    catch (Exception ex)
                    {
                        logger?.Error(ex, $"Error occurred while processing HTTP transport session. Session ID: {mcpClientSessionId}.");
                    }
                    finally
                    {
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
