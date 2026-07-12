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
using System.Collections.Concurrent;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// Per-MCP-session sticky engine-instance selection (mcp-authorize b4, design doc 04 step 2).
    /// Keyed by the MCP session id (the <c>Mcp-Session-Id</c> header). <c>select_engine_instance</c>
    /// writes here; the request pipeline reloads the value into
    /// <see cref="Auth.McpSessionTokenContext.CurrentSelectedInstanceId"/> on each subsequent request
    /// so routing honors the selection. Selection is per-SESSION, NOT per-account — two agent
    /// sessions of the same account may independently select different instances (design 04
    /// multi-tenancy semantics). A selection may narrow a pin but never override it to another
    /// project (enforced by <c>select_engine_instance</c> before writing here).
    /// </summary>
    public interface ISessionSelectionStore
    {
        /// <summary>The instance id sticky-selected for <paramref name="sessionId"/>, or null when none.</summary>
        string? Get(string? sessionId);

        /// <summary>Record (or replace) the sticky selection for <paramref name="sessionId"/>.</summary>
        void Set(string sessionId, string instanceId);

        /// <summary>Drop the selection for <paramref name="sessionId"/> (e.g. on session end).</summary>
        void Clear(string? sessionId);
    }

    /// <summary>
    /// In-memory <see cref="ISessionSelectionStore"/>. Registered as a singleton only in <c>oauth</c>
    /// mode (the account+instance pairing plane); the map is small (one entry per live agent session)
    /// and entries are dropped on <see cref="Clear"/>.
    /// </summary>
    public sealed class SessionSelectionStore : ISessionSelectionStore
    {
        readonly ConcurrentDictionary<string, string> _selections = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public string? Get(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;
            return _selections.TryGetValue(sessionId!, out var instanceId) ? instanceId : null;
        }

        public void Set(string sessionId, string instanceId)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("sessionId must be non-empty.", nameof(sessionId));
            if (string.IsNullOrEmpty(instanceId))
                throw new ArgumentException("instanceId must be non-empty.", nameof(instanceId));
            _selections[sessionId] = instanceId;
        }

        public void Clear(string? sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId))
                _selections.TryRemove(sessionId!, out _);
        }
    }
}
