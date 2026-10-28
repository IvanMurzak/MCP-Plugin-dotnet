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
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// A connection strategy that tells plugin roles apart (<see cref="ClientRole"/>): the hub hands it
    /// the role a plugin declares at connect and the version handshake's <c>environment</c>. Separate from
    /// <see cref="IMcpConnectionStrategy"/> so existing strategy implementations keep compiling.
    /// </summary>
    public interface IPluginRoleStrategy
    {
        /// <summary>The plugin connections and their roles.</summary>
        PluginConnectionRegistry Registry { get; }

        /// <summary>
        /// <see cref="IMcpConnectionStrategy.OnPluginConnected"/> with the role the plugin declared
        /// (hub query <c>client_role</c> or header <c>X-Client-Role</c>); null when it declared none, and
        /// then the role is decided at <see cref="OnPluginHandshake"/>.
        /// </summary>
        void OnPluginConnected(Type hubType, string connectionId, string? token, ClientRole? declaredRole,
            ILogger logger, Action<string, string?> disconnectClient);

        /// <summary>
        /// The plugin finished a compatible version handshake. A connection without a declared role gets one
        /// from <paramref name="environment"/> now. True when this made the connection the routed editor.
        /// </summary>
        bool OnPluginHandshake(Type hubType, string connectionId, string? environment,
            ILogger logger, Action<string, string?> disconnectClient);

        /// <summary>True (once) when a runtime served agents in place of an editor since the last call.</summary>
        bool TakeRuntimeServed();
    }
}
