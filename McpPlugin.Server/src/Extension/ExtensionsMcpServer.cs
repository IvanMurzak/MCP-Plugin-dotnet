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
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Transport;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
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
                    logger?.Debug("Configuring MCP Server with transport '{0}' and auth strategy '{1}'.",
                        dataArguments.ClientTransport, dataArguments.Authorization);

                    try
                    {
                        // Setup MCP tools
                        options.Capabilities ??= new();
                        options.Capabilities.Tools ??= new();
                        options.Capabilities.Tools.ListChanged = true;
                        options.Handlers.CallToolHandler = async (request, ct) =>
                        {
                            var webhookOptions = request?.Services?.GetService<WebhookOptions>();
                            Stopwatch? stopwatch = null;
                            long requestSize = 0;
                            if (webhookOptions?.IsToolEnabled == true)
                            {
                                stopwatch = Stopwatch.StartNew();
                                requestSize = MeasureSize(request?.Params?.Arguments);
                            }

                            var result = await ToolRouter.Call(request!, ct);

                            if (webhookOptions?.IsToolEnabled == true)
                            {
                                stopwatch!.Stop();
                                var collector = request?.Services?.GetService<IWebhookEventCollector>();
                                if (collector != null)
                                {
                                    var isError = result.IsError == true;
                                    var responseSize = isError ? 0L : MeasureSize(result);
                                    var errorDetails = isError ? ExtractErrorMessage(result) : null;
                                    collector.OnToolCall(
                                        request!.Params?.Name ?? "unknown",
                                        requestSize,
                                        responseSize,
                                        isError ? "failure" : "success",
                                        stopwatch.ElapsedMilliseconds,
                                        errorDetails);
                                }
                            }

                            return result;
                        };
                        options.Handlers.ListToolsHandler = ToolRouter.ListAll;

                        // Setup MCP resources
                        options.Capabilities.Resources ??= new();
                        options.Capabilities.Resources.ListChanged = true;
                        options.Handlers.ReadResourceHandler = async (request, ct) =>
                        {
                            var result = await ResourceRouter.Read(request, ct);

                            var webhookOptions = request?.Services?.GetService<WebhookOptions>();
                            if (webhookOptions?.IsResourceEnabled == true)
                            {
                                var collector = request?.Services?.GetService<IWebhookEventCollector>();
                                if (collector != null)
                                {
                                    var responseSize = MeasureSize(result);
                                    collector.OnResourceAccessed(
                                        request!.Params?.Uri ?? "unknown",
                                        responseSize);
                                }
                            }

                            return result;
                        };
                        options.Handlers.ListResourcesHandler = ResourceRouter.List;
                        options.Handlers.ListResourceTemplatesHandler = ResourceRouter.ListTemplates;

                        // Setup MCP prompts
                        options.Capabilities.Prompts ??= new();
                        options.Capabilities.Prompts.ListChanged = true;
                        options.Handlers.GetPromptHandler = async (request, ct) =>
                        {
                            var result = await PromptRouter.Get(request, ct);

                            var webhookOptions = request?.Services?.GetService<WebhookOptions>();
                            if (webhookOptions?.IsPromptEnabled == true)
                            {
                                var collector = request?.Services?.GetService<IWebhookEventCollector>();
                                if (collector != null)
                                {
                                    var responseSize = MeasureSize(result);
                                    collector.OnPromptRetrieved(
                                        request!.Params?.Name ?? "unknown",
                                        responseSize);
                                }
                            }

                            return result;
                        };
                        options.Handlers.ListPromptsHandler = PromptRouter.List;
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex, "Error configuring MCP Server: {0}", ex.Message);
                        throw;
                    }
                });

            // Delegate transport configuration
            mcpServerBuilder = transport.ConfigureTransport(mcpServerBuilder, dataArguments, logger);

            var setup = new McpServerSetup(strategy, transport);

            // Register factories and resolved instances in DI
            services.AddSingleton<ITransportFactory>(transportFactory);
            services.AddSingleton<ITransportLayer>(transport);
            services.AddSingleton<IMcpStrategyFactory>(strategyFactory);
            services.AddSingleton<IMcpConnectionStrategy>(strategy);
            services.AddSingleton(setup);

            return mcpServerBuilder;
        }

        static long MeasureSize(object? obj)
        {
            if (obj == null)
                return 0;

            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(obj).Length;
            }
            catch (JsonException ex)
            {
                LogManager.GetCurrentClassLogger().Debug(ex, "Failed to measure size of webhook payload.");
                return 0;
            }
        }

        static string? ExtractErrorMessage(ModelContextProtocol.Protocol.CallToolResult result)
        {
            if (result.Content == null || result.Content.Count == 0)
                return null;

            var textContent = result.Content
                .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .FirstOrDefault();

            return textContent?.Text;
        }
    }
}
