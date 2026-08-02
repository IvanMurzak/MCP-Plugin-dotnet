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
    /// writes here; <see cref="Auth.McpSessionTokenMiddleware"/> reloads the value into
    /// <see cref="Auth.McpSessionTokenContext.CurrentSelectedInstanceId"/> on each subsequent request,
    /// which is what the direct-tool REST surfaces observe.
    /// <para>On the streamable-HTTP MCP path that per-request reload does NOT reach a request handler
    /// (<c>PerSessionExecutionContext</c> — see <see cref="Auth.McpSessionTokenContext.CurrentSessionId"/>),
    /// so <see cref="Strategy.AccountMcpStrategy"/> reads this store DIRECTLY, keyed by the ambient
    /// session id, when the ambient selection is absent (issue #195). Routing therefore honors a
    /// selection on every subsequent request of the session, not just the one that set it. The strategy
    /// OWNS the instance registered as this interface's singleton, so the writer and the reader cannot
    /// drift onto two different maps.</para>
    /// <para>Selection is per-SESSION, NOT per-account — two agent sessions of the same account may
    /// independently select different instances (design 04 multi-tenancy semantics). A selection may
    /// narrow a pin but never override it to another project (enforced by <c>select_engine_instance</c>
    /// before writing here, and again by <see cref="Strategy.AccountInstances.Resolve"/>, which applies
    /// the pin before the sticky term).</para>
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
