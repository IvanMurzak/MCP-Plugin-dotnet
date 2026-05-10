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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Shared helpers for the MCP <c>list</c> routers — <c>tools/list</c>,
    /// <c>prompts/list</c>, <c>resources/list</c>,
    /// <c>resources/templates/list</c>. Two concerns:
    /// <list type="bullet">
    ///   <item><description>Visibility filtering keyed off the trusted-internal-client
    ///   flag (<see cref="SelectVisible{TIn, TOut}"/>).</description></item>
    ///   <item><description>The <c>_meta</c> object emitted on the resulting
    ///   <see cref="ModelContextProtocol.Protocol.Tool"/>,
    ///   <see cref="ModelContextProtocol.Protocol.Prompt"/>,
    ///   <see cref="ModelContextProtocol.Protocol.Resource"/>,
    ///   <see cref="ModelContextProtocol.Protocol.ResourceTemplate"/> primitives
    ///   (<see cref="BuildEnabledMeta"/>).</description></item>
    /// </list>
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

        /// <summary>
        /// Filters a list-router's source primitives to the set the current
        /// caller is allowed to see, then projects each survivor through
        /// <paramref name="project"/>. Trusted internal clients (callers that
        /// sent <c>X-McpPlugin-Internal-Client: 1</c> — see
        /// <see cref="com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server.Headers.TrustedInternalClient"/>
        /// and <see cref="McpSessionTokenContext.IsTrustedInternalClient"/>)
        /// receive the FULL catalog including disabled primitives; every other
        /// caller continues to see only entries where <paramref name="getEnabled"/>
        /// returns <see langword="true"/>. Null elements are dropped in both modes.
        /// </summary>
        public static List<TOut> SelectVisible<TIn, TOut>(
            this IEnumerable<TIn?> source,
            Func<TIn, bool> getEnabled,
            Func<TIn, TOut> project)
            where TIn : class
        {
            var includeDisabled = McpSessionTokenContext.IsTrustedInternalClient;
            return source
                .Where(x => x != null && (includeDisabled || getEnabled(x)))
                .Select(x => project(x!))
                .ToList();
        }
    }
}
