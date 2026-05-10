/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Shared helpers for the <c>_meta</c> object the MCP <c>list</c> routers
    /// attach to <see cref="ModelContextProtocol.Protocol.Tool"/>,
    /// <see cref="ModelContextProtocol.Protocol.Prompt"/>,
    /// <see cref="ModelContextProtocol.Protocol.Resource"/>, and
    /// <see cref="ModelContextProtocol.Protocol.ResourceTemplate"/> primitives.
    /// </summary>
    /// <remarks>
    /// MCP's <c>_meta</c> field is reserved for protocol-level metadata that
    /// servers may emit and clients may ignore (per spec). We use it to surface
    /// the plugin-side <c>Enabled</c> flag to trusted internal clients that
    /// have opted in to the unfiltered catalog — see
    /// <see cref="com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server.Headers.TrustedInternalClient"/>.
    /// </remarks>
    public static class ExtensionsListMeta
    {
        /// <summary>Property name used on the <c>_meta</c> object.</summary>
        public const string EnabledKey = "enabled";

        /// <summary>
        /// Returns a <see cref="JsonObject"/> carrying <c>{ "enabled": false }</c>
        /// when the primitive is disabled; returns <see langword="null"/> for
        /// enabled primitives so the wire shape for the default case is
        /// unchanged.
        /// </summary>
        public static JsonObject? BuildEnabledMeta(bool enabled)
            => enabled ? null : new JsonObject { [EnabledKey] = false };
    }
}
