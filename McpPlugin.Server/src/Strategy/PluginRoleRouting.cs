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
using System.Threading;
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The plugin role the current request must reach, flowing with the async call (like
    /// <see cref="Auth.McpSessionTokenContext"/>). Set around a call of a tool that only a runtime has, so
    /// <see cref="SingleProjectMcpStrategy.ResolveConnectionId"/> picks the runtime even while an editor is
    /// connected. Unset, routing is unchanged (the editor first).
    /// </summary>
    public static class PluginRoleRouting
    {
        static readonly AsyncLocal<ClientRole?> _preferred = new AsyncLocal<ClientRole?>();

        /// <summary>The role the current request must reach, or null for the default routing.</summary>
        public static ClientRole? Preferred => _preferred.Value;

        /// <summary>Routes the current async flow to <paramref name="role"/> until the returned scope is disposed.</summary>
        public static IDisposable Prefer(ClientRole role)
        {
            var previous = _preferred.Value;
            _preferred.Value = role;
            return new Scope(previous);
        }

        sealed class Scope : IDisposable
        {
            readonly ClientRole? _previous;
            public Scope(ClientRole? previous) => _previous = previous;
            public void Dispose() => _preferred.Value = _previous;
        }
    }
}
