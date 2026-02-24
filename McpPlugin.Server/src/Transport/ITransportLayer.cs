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
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    public interface ITransportLayer
    {
        Consts.MCP.Server.TransportMethod TransportMethod { get; }

        /// <summary>
        /// Configures MCP SDK transport on the IMcpServerBuilder.
        /// Called during DI service registration.
        /// </summary>
        IMcpServerBuilder ConfigureTransport(
            IMcpServerBuilder mcpServerBuilder,
            DataArguments dataArguments,
            Logger? logger = null);

        /// <summary>
        /// Registers any additional services required by this transport
        /// (e.g., HostedService for stdio).
        /// </summary>
        void ConfigureServices(IServiceCollection services, DataArguments dataArguments);

        /// <summary>
        /// Maps any middleware/endpoints on the WebApplication
        /// (e.g., MapMcp for streamableHttp).
        /// </summary>
        void ConfigureApp(WebApplication app, DataArguments dataArguments);
    }
}
