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
        /// <summary>The project-pinned route family root — <c>/p/{pin}</c> and everything beneath it.</summary>
        const string PinnedRoot = "/p";

        /// <summary>
        /// The UNPINNED REST tool surfaces. They can EXECUTE tools, so they need exactly the same
        /// rebinding defense as the MCP endpoint (their pinned twins are covered by <see cref="PinnedRoot"/>).
        /// </summary>
        static readonly string[] RestToolSurfaces = { "/api/tools", "/api/system-tools" };

        /// <summary>
        /// True when the request path is one the transport MUST guard:
        /// <list type="bullet">
        ///   <item>the MCP endpoints (<c>/</c>, <c>/mcp</c>, <c>/mcp/*</c>) and the SignalR hub (incl. <c>/negotiate</c>);</item>
        ///   <item>every project-pinned route (<c>/p</c>, <c>/p/{pin}</c>, <c>/p/{pin}/…</c>) — that family covers the
        ///   nginx-stripped pinned MCP endpoint <b>and</b> both pinned REST tool groups;</item>
        ///   <item>the unpinned REST tool surfaces <c>/api/tools[/…]</c> and <c>/api/system-tools[/…]</c>.</item>
        /// </list>
        /// Discovery paths (<c>/.well-known/*</c>, <c>/oauth/*</c>) are intentionally NOT guarded.
        ///
        /// <para><b>Why the REST/pinned families are guarded (2026-07-25).</b> Guarding only <c>/mcp*</c> left every
        /// tool-EXECUTING REST route outside the DNS-rebinding defense: a hostile page could drive
        /// <c>POST /api/tools/{name}</c>, <c>POST /api/system-tools/{name}</c> or their <c>/p/{pin}/…</c> variants from
        /// the victim's own browser — unauthenticated in <c>none</c> mode. The bare pinned MCP endpoint <c>/p/{pin}</c>
        /// (what the server receives after nginx strips <c>/mcp</c>) was unguarded for the same reason, while its
        /// <c>/mcp/p/{pin}</c> twin was guarded by the <c>/mcp/</c> prefix — an incoherence this closes. Only
        /// <c>/api/tools</c> and <c>/api/system-tools</c> are added from <c>/api/*</c>: guarding <c>/api/*</c>
        /// wholesale would capture a hosting application's own unrelated endpoints.</para>
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

            // Project-pinned family: /p, /p/{pin}, /p/{pin}/api/tools[/…], /p/{pin}/api/system-tools[/…].
            // Guarded as a whole so a future pinned route is covered by construction, never by a later edit.
            if (IsSegmentPrefix(value, PinnedRoot))
                return true;

            foreach (var surface in RestToolSurfaces)
            {
                if (IsSegmentPrefix(value, surface))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="value"/> equals <paramref name="prefix"/> exactly, or extends it at a
        /// SEGMENT boundary (<c>/api/tools</c> and <c>/api/tools/x</c> match; <c>/api/toolsX</c> does not).
        /// </summary>
        static bool IsSegmentPrefix(string value, string prefix)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return value.Length == prefix.Length || value[prefix.Length] == '/';
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
