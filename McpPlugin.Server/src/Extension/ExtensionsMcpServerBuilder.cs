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
        public static IMcpServerBuilder WithMcpPluginServer(this IMcpServerBuilder mcpServerBuilder, DataArguments dataArguments, Version version)
        {
            if (mcpServerBuilder == null)
                throw new System.ArgumentNullException(nameof(mcpServerBuilder));

            if (dataArguments == null)
                throw new System.ArgumentNullException(nameof(dataArguments));

            mcpServerBuilder.Services.AddSingleton<IDataArguments>(dataArguments);
            mcpServerBuilder.Services.AddSingleton(version);

            mcpServerBuilder.Services.AddRouting();
            if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
                mcpServerBuilder.Services.AddHostedService<McpServerService>();

            mcpServerBuilder.Services.AddSingleton<HubEventToolsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventPromptsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventResourcesChange>();
            mcpServerBuilder.Services.AddSingleton<IRequestTrackingService, RequestTrackingService>();
            mcpServerBuilder.Services.AddSingleton<IClientToolHub, RemoteToolRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientPromptHub, RemotePromptRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientResourceHub, RemoteResourceRunner>();

            // builder.AddMcpRunner();

            return mcpServerBuilder;
        }
    }
}
