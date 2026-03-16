/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// Reads the <see cref="TokenAuthenticationHandler.TokenClaimType"/> claim
    /// from <see cref="HttpContext.User"/> (set by <see cref="TokenAuthenticationHandler"/>)
    /// and propagates it into <see cref="McpSessionTokenContext.CurrentToken"/>.
    /// Must run after <c>UseAuthentication()</c> and before endpoint handlers.
    /// </summary>
    public class McpSessionTokenMiddleware
    {
        private readonly RequestDelegate _next;

        public McpSessionTokenMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context)
        {
            var tokenClaim = context.User?.FindFirst(TokenAuthenticationHandler.TokenClaimType);
            if (tokenClaim != null)
                McpSessionTokenContext.CurrentToken = tokenClaim.Value;

            await _next(context);
        }
    }
}
