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
        private const int DefaultTokenExpirationSeconds = 3600;

        public Consts.MCP.Server.TransportMethod TransportMethod
            => Consts.MCP.Server.TransportMethod.streamableHttp;

        public IMcpServerBuilder ConfigureTransport(
            IMcpServerBuilder mcpServerBuilder,
            DataArguments dataArguments,
            Logger? logger = null)
        {
            return mcpServerBuilder.WithHttpTransport(options =>
            {
                logger?.Debug($"Http transport configuration.");

                options.Stateless = false;
                options.PerSessionExecutionContext = true;
                options.IdleTimeout = TimeSpan.FromSeconds(30);
                options.RunSessionHandler = async (context, server, cancellationToken) =>
                {
                    logger?.Debug("-------------------------------------------------\nRunning session handler for HTTP transport. Session ID: {sessionId}",
                        server.SessionId);

                    var mcpClientSessionId = server.SessionId ?? throw new InvalidOperationException("MCP Server session ID is not available.");

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        context.RequestAborted);
                    var linkedToken = linkedCts.Token;

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
            });
        }

        public void ConfigureServices(IServiceCollection services, DataArguments dataArguments)
        {
            // HTTP transport does not register a hosted service
        }

        public void ConfigureApp(WebApplication app, DataArguments dataArguments)
        {
            var requireAuth = !string.IsNullOrEmpty(dataArguments.Token)
                || dataArguments.Authorization == Consts.MCP.Server.AuthOption.required;

            if (requireAuth)
            {
                // MCP: OAuth 2.0 Protected Resource Metadata (RFC 9728)
                // Tells clients that this server is its own authorization server
                app.MapGet("/.well-known/oauth-protected-resource", async (HttpContext ctx) =>
                {
                    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                    await Results.Ok(new
                    {
                        resource = baseUrl,
                        authorization_servers = new[] { baseUrl }
                    }).ExecuteAsync(ctx);
                }).AllowAnonymous();

                // MCP: OAuth 2.0 Authorization Server Metadata (RFC 8414)
                // Describes the token and registration endpoints so clients can authenticate
                app.MapGet("/.well-known/oauth-authorization-server", async (HttpContext ctx) =>
                {
                    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                    await Results.Ok(new
                    {
                        issuer = baseUrl,
                        token_endpoint = $"{baseUrl}/oauth/token",
                        registration_endpoint = $"{baseUrl}/oauth/register",
                        grant_types_supported = new[] { "client_credentials" },
                        token_endpoint_auth_methods_supported = new[] { "client_secret_post" }
                    }).ExecuteAsync(ctx);
                }).AllowAnonymous();

                // MCP: Dynamic Client Registration (RFC 7591)
                // Open registration — any client can self-register and receive unique credentials
                app.MapPost("/oauth/register", async (HttpContext ctx) =>
                {
                    string? clientName = null;
                    if (ctx.Request.ContentType?.Contains("application/json") == true)
                    {
                        try
                        {
                            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
                            if (doc.RootElement.TryGetProperty("client_name", out var nameProp)
                                && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                clientName = nameProp.GetString();
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            await Results.Json(new { error = "invalid_request", error_description = "Malformed JSON body." }, statusCode: 400).ExecuteAsync(ctx);
                            return;
                        }
                    }

                    var client = ClientRegistrationStore.Register(clientName);
                    await Results.Json(new
                    {
                        client_id = client.ClientId,
                        client_secret = client.ClientSecret,
                        client_id_issued_at = client.IssuedAt.ToUnixTimeSeconds(),
                        grant_types = new[] { "client_credentials" },
                        token_endpoint_auth_method = "client_secret_post"
                    }, statusCode: 201).ExecuteAsync(ctx);
                }).AllowAnonymous();

                // MCP: Token endpoint — two authentication paths:
                //   Path A (DCR): client_id + registered client_secret → unique access_token
                //   Path B (legacy): client_secret == --token → returns --token as access_token
                app.MapPost("/oauth/token", async (HttpContext ctx) =>
                {
                    var form = await ctx.Request.ReadFormAsync();
                    var grantType = form["grant_type"].ToString();
                    var clientId = form["client_id"].ToString();
                    var clientSecret = form["client_secret"].ToString();

                    IResult result;
                    if (grantType != "client_credentials")
                    {
                        result = Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
                    }
                    else if (!string.IsNullOrEmpty(clientId))
                    {
                        // Path A: registered client (DCR flow)
                        var accessToken = ClientRegistrationStore.IssueAccessToken(clientId, clientSecret);
                        result = accessToken != null
                            ? Results.Ok(new
                            {
                                access_token = accessToken,
                                token_type = "Bearer",
                                expires_in = DefaultTokenExpirationSeconds
                            })
                            : Results.Json(new { error = "invalid_client" }, statusCode: 401);
                    }
                    else
                    {
                        // Path B: legacy — client_secret == --token (backward compat)
                        result = string.IsNullOrEmpty(clientSecret) || clientSecret != dataArguments.Token
                            ? Results.Json(new { error = "invalid_client" }, statusCode: 401)
                            : Results.Ok(new
                            {
                                access_token = dataArguments.Token,
                                token_type = "Bearer",
                                expires_in = DefaultTokenExpirationSeconds
                            });
                    }

                    await result.ExecuteAsync(ctx);
                }).AllowAnonymous();

                app.MapMcp("/").RequireAuthorization();
                app.MapMcp("/mcp").RequireAuthorization();
            }
            else
            {
                app.MapMcp("/");
                app.MapMcp("/mcp");
            }
        }

    }
}
