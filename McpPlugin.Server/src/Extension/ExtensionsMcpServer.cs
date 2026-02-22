/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServer
    {
        public static IMcpServerBuilder WithMcpServer(
            this IServiceCollection services,
            DataArguments dataArguments,
            Logger? logger = null)
        {
            var transportFactory = new TransportFactory();
            var strategyFactory = new McpStrategyFactory();

            var transport = transportFactory.Create(dataArguments.ClientTransport);
            var strategy = strategyFactory.Create(dataArguments.Authorization);

            // Validate configuration for the selected deployment mode
            strategy.Validate(dataArguments);

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

            // Delegate transport configuration
            mcpServerBuilder = transport.ConfigureTransport(mcpServerBuilder, dataArguments, logger);

            // Register factories and resolved instances in DI
            services.AddSingleton<ITransportFactory>(transportFactory);
            services.AddSingleton<ITransportLayer>(transport);
            services.AddSingleton<IMcpStrategyFactory>(strategyFactory);
            services.AddSingleton<IMcpConnectionStrategy>(strategy);

            return mcpServerBuilder;
        }
    }
}
