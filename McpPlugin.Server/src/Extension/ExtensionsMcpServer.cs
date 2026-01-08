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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServer
    {
        public static IMcpServerBuilder WithMcpServer(
            this IServiceCollection services,
            Consts.MCP.Server.TransportMethod mcpClientTransport,
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

            if (mcpClientTransport == Consts.MCP.Server.TransportMethod.stdio)
            {
                // Configure STDIO transport
                mcpServerBuilder = mcpServerBuilder.WithStdioServerTransport();
            }
            else if (mcpClientTransport == Consts.MCP.Server.TransportMethod.streamableHttp)
            {
                // Configure HTTP transport
                mcpServerBuilder = mcpServerBuilder.WithHttpTransport(options =>
                {
                    logger?.Debug($"Http transport configuration.");

                    options.Stateless = false;
                    options.PerSessionExecutionContext = true;
                    options.RunSessionHandler = async (context, server, cancellationToken) =>
                    {
                        var connectionGuid = Guid.NewGuid();
                        try
                        {
                            // This is where you can run logic before a session starts
                            // For example, you can log the session start or initialize resources
                            logger?.Debug($"----------\nRunning session handler for HTTP transport. Connection guid: {connectionGuid}");

                            var service = new McpServerService(
                                server.Services!.GetRequiredService<ILogger<McpServerService>>(),
                                server.Services!.GetRequiredService<IClientToolHub>(),
                                server.Services!.GetRequiredService<IClientPromptHub>(),
                                server.Services!.GetRequiredService<IClientResourceHub>(),
                                server.Services!.GetRequiredService<HubEventToolsChange>(),
                                server.Services!.GetRequiredService<HubEventPromptsChange>(),
                                server.Services!.GetRequiredService<HubEventResourcesChange>(),
                                mcpServer: server,
                                mcpSession: null
                            );

                            try
                            {
                                await service.StartAsync(cancellationToken);
                                await server.RunAsync(cancellationToken);
                            }
                            finally
                            {
                                await service.StopAsync(cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.Error(ex, $"Error occurred while processing HTTP transport session. Connection guid: {connectionGuid}.");
                        }
                        finally
                        {
                            logger?.Debug($"Session handler for HTTP transport completed. Connection guid: {connectionGuid}\n----------");
                        }
                    };
                });
            }
            else
            {
                throw new ArgumentException($"Unsupported transport method: {mcpClientTransport}. " +
                    $"Supported methods are: {Consts.MCP.Server.TransportMethod.stdio}, {Consts.MCP.Server.TransportMethod.streamableHttp}");
            }
            return mcpServerBuilder;
        }
    }
}
