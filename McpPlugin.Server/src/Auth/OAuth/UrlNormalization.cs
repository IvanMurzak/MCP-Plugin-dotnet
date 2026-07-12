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
using System.Net;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// URL normalization helpers shared by strict-<c>aud</c> validation and Origin validation
    /// (mcp-authorize b2). Loopback aliases (<c>localhost</c> / <c>127.0.0.1</c> / <c>::1</c>) are
    /// normalized to a single canonical host so a token audienced to <c>http://localhost:23471</c>
    /// matches an RS whose <c>--public-url</c> is <c>http://127.0.0.1:23471</c> and vice versa.
    /// </summary>
    public static class UrlNormalization
    {
        private const string CanonicalLoopbackHost = "localhost";

        /// <summary>
        /// True when <paramref name="host"/> is a loopback name/address
        /// (<c>localhost</c>, <c>127.0.0.0/8</c>, or IPv6 <c>::1</c>).
        /// </summary>
        public static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            var trimmed = host!.Trim().Trim('[', ']');
            if (string.Equals(trimmed, CanonicalLoopbackHost, StringComparison.OrdinalIgnoreCase))
                return true;

            if (IPAddress.TryParse(trimmed, out var ip))
                return IPAddress.IsLoopback(ip);

            return false;
        }

        /// <summary>
        /// Canonicalize a resource URI (the token <c>aud</c> / the RS <c>--public-url</c>) into a
        /// comparable string: lowercased scheme, loopback-aliased + lowercased host, explicit port
        /// (default ports normalized by <see cref="Uri"/>), and a trailing-slash-stripped path.
        /// Returns <c>null</c> when the value is not an absolute URI.
        /// </summary>
        public static string? NormalizeResource(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri))
                return null;

            var host = IsLoopbackHost(uri.Host) ? CanonicalLoopbackHost : uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath;
            if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
                path = path.TrimEnd('/');
            if (path == "/")
                path = string.Empty;

            return $"{uri.Scheme.ToLowerInvariant()}://{host}:{uri.Port}{path}";
        }

        /// <summary>
        /// Canonicalize an <c>Origin</c> header value into a comparable <c>scheme://host:port</c>
        /// (no path) string, loopback-aliased. Returns <c>null</c> when the value is not a valid
        /// absolute origin.
        /// </summary>
        public static string? NormalizeOrigin(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return null;

            if (!Uri.TryCreate(origin!.Trim(), UriKind.Absolute, out var uri))
                return null;

            var host = IsLoopbackHost(uri.Host) ? CanonicalLoopbackHost : uri.Host.ToLowerInvariant();
            return $"{uri.Scheme.ToLowerInvariant()}://{host}:{uri.Port}";
        }

        /// <summary>
        /// True when two resource identifiers are equal after normalization (loopback-aliased).
        /// </summary>
        public static bool ResourcesMatch(string? a, string? b)
        {
            var na = NormalizeResource(a);
            var nb = NormalizeResource(b);
            return na != null && nb != null && string.Equals(na, nb, StringComparison.Ordinal);
        }
    }
}
