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
using com.IvanMurzak.McpPlugin.Server.Api;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
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

            var forwardedHeadersOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            forwardedHeadersOptions.KnownNetworks.Clear();
            forwardedHeadersOptions.KnownProxies.Clear();
            app.UseForwardedHeaders(forwardedHeadersOptions);

            // Setup routing ----------------------------------------------------
            app.UseRouting();

            // Setup auth -------------------------------------------------------
            app.UseAuthentication();
            app.UseMiddleware<McpSessionTokenMiddleware>();
            app.UseAuthorization();

            // Setup SignalR ----------------------------------------------------
            app.MapHub<McpServerHub>(Consts.Hub.RemoteApp, options =>
            {
                options.Transports = HttpTransports.All;
                options.ApplicationMaxBufferSize = 1024 * 1024 * 4; // 4 MB
                options.TransportMaxBufferSize = 1024 * 1024 * 4; // 4 MB
            });

            // Delegate MCP endpoint mapping to transport layer
            var transport = app.Services.GetRequiredService<ITransportLayer>();
            transport.ConfigureApp(app, dataArguments);

            // Setup direct tool call API (POST /api/tools/{name}, GET /api/tools)
            app.MapDirectToolCallApi(dataArguments);
            // Setup system tool API (POST /api/system-tools/{name}) — internal tools not exposed to MCP
            app.MapSystemToolApi(dataArguments);

            return app;
        }
    }
}
