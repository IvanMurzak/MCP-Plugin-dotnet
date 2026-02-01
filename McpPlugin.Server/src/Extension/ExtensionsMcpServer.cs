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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServer
    {
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

                        try
                        {
                            var services = server.Services ?? throw new InvalidOperationException("MCP Server services are not available.");
                            var service = new McpServerService(
                                services.GetRequiredService<ILogger<McpServerService>>(),
                                services.GetRequiredService<Common.Version>(),
                                dataArguments,
                                services.GetRequiredService<IClientToolHub>(),
                                services.GetRequiredService<IClientPromptHub>(),
                                services.GetRequiredService<IClientResourceHub>(),
                                services.GetRequiredService<HubEventToolsChange>(),
                                services.GetRequiredService<HubEventPromptsChange>(),
                                services.GetRequiredService<HubEventResourcesChange>(),
                                services.GetRequiredService<IHubContext<McpServerHub, IClientMcpRpc>>(),
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
            else
            {
                throw new ArgumentException($"Unsupported transport method: {dataArguments.ClientTransport}. " +
                    $"Supported methods are: {Consts.MCP.Server.TransportMethod.stdio}, {Consts.MCP.Server.TransportMethod.streamableHttp}");
            }
            return mcpServerBuilder;
        }
    }
}
