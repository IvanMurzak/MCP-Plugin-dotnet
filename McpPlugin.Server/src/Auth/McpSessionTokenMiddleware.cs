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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// Propagates the bearer token into <see cref="McpSessionTokenContext.CurrentToken"/>
    /// so downstream services (e.g. RemoteToolRunner) can route calls to the correct plugin.
    ///
    /// Resolution order:
    ///   1. <see cref="TokenAuthenticationHandler.TokenClaimType"/> claim on <see cref="HttpContext.User"/>
    ///      (present when auth is required and the handler validated the token).
    ///   2. Raw <c>Authorization: Bearer …</c> header fallback — covers the case where auth
    ///      is not required (<see cref="TokenAuthenticationHandler"/> returns <c>NoResult</c>)
    ///      but the caller still sends a token for plugin routing.
    ///
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
            {
                McpSessionTokenContext.CurrentToken = tokenClaim.Value;
            }
            else
            {
                // Fallback: extract from raw header when auth handler did not populate claims
                // (e.g. auth not required, but caller sends a token for plugin routing).
                var authHeader = context.Request.Headers.Authorization.ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    McpSessionTokenContext.CurrentToken = authHeader.Substring("Bearer ".Length).Trim();
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(remoteIp))
                McpSessionTokenContext.CurrentClientIp = remoteIp;

            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(userAgent))
                McpSessionTokenContext.CurrentUserAgent = userAgent;

            try
            {
                await _next(context);
            }
            finally
            {
                McpSessionTokenContext.CurrentToken = null;
                McpSessionTokenContext.CurrentClientIp = null;
                McpSessionTokenContext.CurrentUserAgent = null;
            }
        }
    }
}
