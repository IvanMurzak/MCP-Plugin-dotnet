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
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Security
{
    /// <summary>
    /// Pure Origin-validation policy (mcp-authorize b2). Separated from the middleware so the
    /// allow/deny and path-matching logic is unit-testable without an HTTP host.
    /// </summary>
    public static class OriginPolicy
    {
        /// <summary>
        /// True when the request path is one the transport MUST guard: the MCP endpoints
        /// (<c>/</c>, <c>/mcp</c>, <c>/mcp/*</c>) and the SignalR hub (incl. <c>/negotiate</c>).
        /// Discovery paths (<c>/.well-known/*</c>, <c>/oauth/*</c>) are intentionally NOT guarded.
        /// </summary>
        public static bool IsGuardedPath(PathString path, string hubPath)
        {
            if (!path.HasValue)
                return false;

            var value = path.Value!;
            if (value.StartsWith(hubPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "/", StringComparison.Ordinal))
                return true;
            if (string.Equals(value, "/mcp", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.StartsWith("/mcp/", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// True when the request may proceed: no <c>Origin</c> header (native client), a loopback
        /// origin (when allowed), or an explicitly-allowed origin. A present-but-malformed or
        /// present-but-non-allowed origin is rejected (fail closed).
        /// </summary>
        public static bool IsOriginAllowed(string? originHeader, OriginValidationOptions options)
        {
            if (string.IsNullOrEmpty(originHeader))
                return true; // absent → allowed (native, non-browser MCP clients)

            var normalized = UrlNormalization.NormalizeOrigin(originHeader);
            if (normalized == null)
                return false; // present but unparseable → reject

            if (options.AllowLoopback
                && Uri.TryCreate(originHeader!.Trim(), UriKind.Absolute, out var uri)
                && UrlNormalization.IsLoopbackHost(uri.Host))
                return true;

            foreach (var allowed in options.AllowedOrigins)
            {
                if (string.Equals(allowed, normalized, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
