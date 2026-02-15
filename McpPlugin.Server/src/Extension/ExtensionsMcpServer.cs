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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServer
    {
        private static readonly TimeSpan ConnectionHealthCheckInterval = TimeSpan.FromSeconds(5);

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

        public static IMcpServerBuilder WithMcpServer(
            this IServiceCollection services,
            DataArguments dataArguments,
            Logger? logger = null)
        {
            // Setup MCP Server -------------------------------------------------------------
            var mcpServerBuilder = services
                .AddMcpServer(options =>
                {
                    // Setup MCP tools
                    options.Capabilities ??= new();
                    options.Capabilities.Tools ??= new();
                    options.Capabilities.Tools.ListChanged = true;
                    options.Handlers.CallToolHandler = ToolRouter.Call;
                    options.Handlers.ListToolsHandler = ToolRouter.ListAll;

                    // Setup MCP resources
                    options.Capabilities.Resources ??= new();
                    options.Capabilities.Resources.ListChanged = true;
                    options.Handlers.ReadResourceHandler = ResourceRouter.Read;
                    options.Handlers.ListResourcesHandler = ResourceRouter.List;
                    options.Handlers.ListResourceTemplatesHandler = ResourceRouter.ListTemplates;

                    // Setup MCP prompts
                    options.Capabilities.Prompts ??= new();
                    options.Capabilities.Prompts.ListChanged = true;
                    options.Handlers.GetPromptHandler = PromptRouter.Get;
                    options.Handlers.ListPromptsHandler = PromptRouter.List;
                });

            if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
            {
                // Configure STDIO transport
                mcpServerBuilder = mcpServerBuilder.WithStdioServerTransport();
            }
            else if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.streamableHttp)
            {
                // Configure HTTP transport
                mcpServerBuilder = mcpServerBuilder.WithHttpTransport(options =>
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
                                services.GetRequiredService<ILogger<McpServerService>>(),
                                services.GetRequiredService<Common.Version>(),
                                dataArguments,
                                services.GetRequiredService<HubEventToolsChange>(),
                                services.GetRequiredService<HubEventPromptsChange>(),
                                services.GetRequiredService<HubEventResourcesChange>(),
                                services.GetRequiredService<IHubContext<McpServerHub, IClientMcpRpc>>(),
                                services.GetRequiredService<IMcpSessionTracker>(),
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
            else
            {
                throw new ArgumentException($"Unsupported transport method: {dataArguments.ClientTransport}. " +
                    $"Supported methods are: {Consts.MCP.Server.TransportMethod.stdio}, {Consts.MCP.Server.TransportMethod.streamableHttp}");
            }
            return mcpServerBuilder;
        }
    }
}
