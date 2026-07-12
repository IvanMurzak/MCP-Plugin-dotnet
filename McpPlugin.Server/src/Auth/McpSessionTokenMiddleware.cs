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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        /// <summary>The MCP Streamable-HTTP session header (case-insensitive lookup).</summary>
        public const string SessionIdHeader = "Mcp-Session-Id";

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

            // OAuth account routing (mcp-authorize b3): resolve the request identity from the validated
            // claims the TokenAuthenticationHandler OAuth path issued (sub → AccountId, scope → Role).
            // Null-safe in legacy modes — no sub claim ⇒ null identity ⇒ token-equality routing unchanged.
            McpSessionTokenContext.CurrentIdentity = ConnectionIdentity.FromPrincipal(context.User);

            // Capture the project pin from the config URL's trailing /p/<pin> segment (design 04 D14).
            McpSessionTokenContext.CurrentProjectPin = TryExtractProjectPin(context.Request.Path.Value);

            // Capture the MCP session id (mcp-authorize b4) and reload this session's sticky
            // engine-instance selection so account routing honors it (design 04 step 2). The store is
            // registered only in oauth mode — null in legacy/no-auth modes leaves selection untouched.
            var sessionId = context.Request.Headers.TryGetValue(SessionIdHeader, out var sid) ? sid.ToString() : null;
            McpSessionTokenContext.CurrentSessionId = string.IsNullOrEmpty(sessionId) ? null : sessionId;
            var selectionStore = context.RequestServices?.GetService<ISessionSelectionStore>();
            if (selectionStore != null)
                McpSessionTokenContext.CurrentSelectedInstanceId = selectionStore.Get(sessionId);

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (!string.IsNullOrEmpty(remoteIp))
                McpSessionTokenContext.CurrentClientIp = remoteIp;

            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(userAgent))
                McpSessionTokenContext.CurrentUserAgent = userAgent;

            if (context.Request.Headers.TryGetValue(Consts.MCP.Server.Headers.TrustedInternalClient, out var trustedHeader))
            {
                McpSessionTokenContext.IsTrustedInternalClient = string.Equals(
                    trustedHeader.ToString(),
                    Consts.MCP.Server.Headers.TrustedInternalClientOptInValue,
                    StringComparison.Ordinal);
            }

            try
            {
                await _next(context);
            }
            finally
            {
                McpSessionTokenContext.CurrentToken = null;
                McpSessionTokenContext.CurrentClientIp = null;
                McpSessionTokenContext.CurrentUserAgent = null;
                McpSessionTokenContext.IsTrustedInternalClient = false;
                McpSessionTokenContext.CurrentIdentity = null;
                McpSessionTokenContext.CurrentProjectPin = null;
                McpSessionTokenContext.CurrentSelectedInstanceId = null;
                McpSessionTokenContext.CurrentSessionId = null;
            }
        }

        /// <summary>
        /// Extracts the project pin from a request path whose config URL ends in a
        /// <c>/p/&lt;pin&gt;</c> segment (design 04 D14). Returns the LAST such <c>&lt;pin&gt;</c>
        /// (the config URL suffix), or null when absent. The pin is the first 8 hex chars of the
        /// project SHA-256; validated loosely as 1–64 hex chars so a malformed segment is ignored.
        /// </summary>
        public static string? TryExtractProjectPin(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path!.Split('/');
            string? pin = null;
            for (var i = 0; i + 1 < segments.Length; i++)
            {
                if (!string.Equals(segments[i], "p", StringComparison.Ordinal))
                    continue;
                var candidate = segments[i + 1];
                if (IsHex(candidate))
                    pin = candidate.ToLowerInvariant();
            }
            return pin;
        }

        static bool IsHex(string? s)
        {
            if (string.IsNullOrEmpty(s) || s!.Length > 64)
                return false;
            foreach (var c in s)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }
    }
}
