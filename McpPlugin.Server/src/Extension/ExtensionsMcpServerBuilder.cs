/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServerBuilder
    {
        public static IServiceCollection WithServerFeatures(this IServiceCollection services, DataArguments dataArguments)
        {
            services.AddRouting();
            if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
                services.AddHostedService<McpServerService>();

            services.AddSingleton<HubEventToolsChange>();
            services.AddSingleton<HubEventPromptsChange>();
            services.AddSingleton<HubEventResourcesChange>();
            services.AddSingleton<IRequestTrackingService, RequestTrackingService>();
            services.AddSingleton<IClientToolHub, RemoteToolRunner>();
            services.AddSingleton<IClientPromptHub, RemotePromptRunner>();
            services.AddSingleton<IClientResourceHub, RemoteResourceRunner>();

            // builder.AddMcpRunner();

            return services;
        }
    }
}
