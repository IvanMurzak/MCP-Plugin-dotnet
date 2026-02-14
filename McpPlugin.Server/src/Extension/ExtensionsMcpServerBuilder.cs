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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsMcpServerBuilder
    {
        public static IMcpServerBuilder WithMcpPluginServer(
            this IMcpServerBuilder mcpServerBuilder,
            DataArguments dataArguments,
            Action<Microsoft.AspNetCore.SignalR.HubOptions>? signalRConfigure = null,
            Common.Version? version = null)
        {
            if (mcpServerBuilder == null)
                throw new ArgumentNullException(nameof(mcpServerBuilder));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var reflector = new Reflector();

            signalRConfigure ??= configure =>
            {
                configure.EnableDetailedErrors = false;
                configure.MaximumReceiveMessageSize = 1024 * 1024 * 256; // 256 MB
                configure.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
                configure.KeepAliveInterval = TimeSpan.FromSeconds(30);
                configure.HandshakeTimeout = TimeSpan.FromMinutes(2);
            };

            version ??= new Common.Version
            {
                Api = Consts.ApiVersion,
                Plugin = Consts.PluginVersion
            };

            mcpServerBuilder.Services
                .AddSignalR(signalRConfigure)
                .AddJsonProtocol(options => SignalR_JsonConfiguration.ConfigureJsonSerializer(reflector, options));

            mcpServerBuilder.Services.AddSingleton<IDataArguments>(dataArguments);
            mcpServerBuilder.Services.AddSingleton(version);

            mcpServerBuilder.Services.AddRouting();
            if (dataArguments.ClientTransport == Consts.MCP.Server.TransportMethod.stdio)
                mcpServerBuilder.Services.AddHostedService<McpServerService>();

            mcpServerBuilder.Services.AddSingleton<HubEventToolsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventPromptsChange>();
            mcpServerBuilder.Services.AddSingleton<HubEventResourcesChange>();
            mcpServerBuilder.Services.AddSingleton<IRequestTrackingService, RequestTrackingService>();
            mcpServerBuilder.Services.AddSingleton<IMcpSessionTracker, McpSessionTracker>();

            mcpServerBuilder.Services.AddSingleton<RemoteToolRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientToolHub>(sp => sp.GetRequiredService<RemoteToolRunner>());

            mcpServerBuilder.Services.AddSingleton<RemotePromptRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientPromptHub>(sp => sp.GetRequiredService<RemotePromptRunner>());

            mcpServerBuilder.Services.AddSingleton<RemoteResourceRunner>();
            mcpServerBuilder.Services.AddSingleton<IClientResourceHub>(sp => sp.GetRequiredService<RemoteResourceRunner>());

            // builder.AddMcpRunner();

            return mcpServerBuilder;
        }
    }
}
