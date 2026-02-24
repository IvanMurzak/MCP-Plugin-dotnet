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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsWebApplication
    {
        public static WebApplication UseMcpPluginServer(this WebApplication app, DataArguments dataArguments)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            // Setup routing ----------------------------------------------------
            app.UseRouting();

            // Setup auth -------------------------------------------------------
            app.UseAuthentication();
            app.UseAuthorization();

            // Setup SignalR ----------------------------------------------------
            app.MapHub<McpServerHub>(Consts.Hub.RemoteApp, options =>
            {
                options.Transports = HttpTransports.All;
                options.ApplicationMaxBufferSize = 1024 * 1024 * 10; // 10 MB
                options.TransportMaxBufferSize = 1024 * 1024 * 10; // 10 MB
            });

            // Delegate MCP endpoint mapping to transport layer
            var transport = app.Services.GetRequiredService<ITransportLayer>();
            transport.ConfigureApp(app, dataArguments);

            return app;
        }
    }
}
