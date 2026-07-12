/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Security
{
    /// <summary>
    /// Enforces Origin validation on the MCP endpoint and the SignalR hub (incl. negotiate) in ALL
    /// auth modes (mcp-authorize b2). A guarded request carrying a present-but-non-allowed
    /// <c>Origin</c> is rejected with <c>403</c> before it reaches routing/auth — the DNS-rebinding
    /// defense for loopback servers (a loopback bind alone does not stop rebinding, since the
    /// hostile request originates from the victim's own browser).
    /// </summary>
    public sealed class OriginValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly OriginValidationOptions _options;

        public OriginValidationMiddleware(RequestDelegate next, OriginValidationOptions options)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (OriginPolicy.IsGuardedPath(context.Request.Path, _options.HubPath))
            {
                var origin = context.Request.Headers["Origin"].ToString();
                if (!OriginPolicy.IsOriginAllowed(origin, _options))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "forbidden_origin",
                        message = "The request Origin is not allowed."
                    }).ConfigureAwait(false);
                    return;
                }
            }

            await _next(context).ConfigureAwait(false);
        }
    }
}
