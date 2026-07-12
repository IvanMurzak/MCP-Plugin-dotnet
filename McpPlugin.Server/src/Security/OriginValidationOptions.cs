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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;

namespace com.IvanMurzak.McpPlugin.Server.Security
{
    /// <summary>
    /// Configuration for Origin validation (mcp-authorize b2), the MCP-transport DNS-rebinding
    /// defense applied in ALL auth modes. A request to the MCP endpoint or the SignalR hub carrying
    /// a present-but-non-allowed <c>Origin</c> is rejected with <c>403</c>.
    /// </summary>
    public sealed class OriginValidationOptions
    {
        /// <summary>
        /// Explicitly-allowed origins (normalized <c>scheme://host:port</c>). Loopback origins are
        /// additionally allowed via <see cref="AllowLoopback"/>. Native (non-browser) MCP clients
        /// send no <c>Origin</c> header and are always allowed.
        /// </summary>
        public IReadOnlyCollection<string> AllowedOrigins { get; }

        /// <summary>When true (default), any loopback origin is allowed regardless of port.</summary>
        public bool AllowLoopback { get; }

        /// <summary>The SignalR hub base path to guard (e.g. <c>/hub/mcp-server</c>).</summary>
        public string HubPath { get; }

        public OriginValidationOptions(IReadOnlyCollection<string> allowedOrigins, bool allowLoopback = true, string? hubPath = null)
        {
            AllowedOrigins = allowedOrigins ?? Array.Empty<string>();
            AllowLoopback = allowLoopback;
            HubPath = hubPath ?? Consts.Hub.RemoteApp;
        }

        /// <summary>
        /// Build from resolved arguments: the RS's own <c>--public-url</c> origin is allowed
        /// (browsers hosted at the RS's own origin), and loopback is always allowed. This covers the
        /// local RS (native clients + same-origin browser clients) and blocks cross-site (DNS
        /// rebinding) origins in every mode.
        /// </summary>
        public static OriginValidationOptions FromArguments(DataArguments dataArguments)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            var publicOrigin = UrlNormalization.NormalizeOrigin(dataArguments.PublicUrl);
            if (publicOrigin != null)
                allowed.Add(publicOrigin);

            return new OriginValidationOptions(allowed, allowLoopback: true);
        }
    }
}
