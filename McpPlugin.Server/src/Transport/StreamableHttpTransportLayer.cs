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
using Microsoft.Extensions.Logging;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    public class StreamableHttpTransportLayer : ITransportLayer
    {
        private static readonly TimeSpan ConnectionHealthCheckInterval = TimeSpan.FromSeconds(5);

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
                options.RunSessionHandler = async (context, server, cancellationToken) =>
                {
                    logger?.Debug("-------------------------------------------------\nRunning session handler for HTTP transport. Session ID: {sessionId}",
                        server.SessionId);

                    var mcpClientSessionId = server.SessionId ?? throw new InvalidOperationException("MCP Server session ID is not available.");

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        context.RequestAborted);
                    var linkedToken = linkedCts.Token;

                    // Start a background task to monitor connection health.
                    // This ensures we detect disconnection even if context.RequestAborted doesn't fire.
                    var connectionMonitorTask = MonitorConnectionHealthAsync(context, linkedCts, logger);

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
                        // Cancel the linked token to stop the connection monitor task
                        if (!linkedCts.IsCancellationRequested)
                            await linkedCts.CancelAsync();
                        try { await connectionMonitorTask; } catch { /* Ignore cancellation exceptions */ }

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
            if (!string.IsNullOrEmpty(dataArguments.Token))
            {
                app.MapMcp("/").RequireAuthorization();
                app.MapMcp("/mcp").RequireAuthorization();
            }
            else
            {
                app.MapMcp("/");
                app.MapMcp("/mcp");
            }
        }

        /// <summary>
        /// Monitors the HTTP connection health and cancels the session when disconnection is detected.
        /// This is necessary because context.RequestAborted may not fire reliably in all disconnect scenarios.
        /// </summary>
        private static async Task MonitorConnectionHealthAsync(
            HttpContext context,
            CancellationTokenSource linkedCts,
            Logger? logger)
        {
            try
            {
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(ConnectionHealthCheckInterval, linkedCts.Token);

                    // Check if the request has been aborted
                    if (context.RequestAborted.IsCancellationRequested)
                    {
                        logger?.Debug("Connection health monitor detected RequestAborted. Cancelling session.");
                        await linkedCts.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the session ends normally
            }
            catch (Exception ex)
            {
                logger?.Debug(ex, "Connection health monitor encountered an error. Cancelling session.");
                try { await linkedCts.CancelAsync(); } catch { /* Ignore */ }
            }
        }
    }
}
