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
using System.Linq;
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The plugin connections of a single-project strategy (<c>none</c>, <c>token</c>) with their
    /// <see cref="ClientRole"/>. A null role means not known yet: the plugin sent no
    /// <c>client_role</c> and has not finished its version handshake. Thread-safe.
    /// </summary>
    public sealed class PluginConnectionRegistry
    {
        readonly object _gate = new object();
        readonly Dictionary<string, Entry> _connections = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Func<DateTimeOffset> _clock;
        long _sequence;
        DateTimeOffset? _lastEditorDisconnect;

        public PluginConnectionRegistry(Func<DateTimeOffset>? clock = null)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Registers a new connection with its role (null when not known yet) and its plugin token.
        /// Returns the connections it replaces (see <see cref="TryClassify"/>); none for an unknown role.
        /// </summary>
        public IReadOnlyList<string> Add(string connectionId, ClientRole? role, string? token)
        {
            lock (_gate)
            {
                // A role declared at connect means an 8.14+ plugin (older plugins declare none).
                _connections[connectionId] = new Entry(role, token, ++_sequence) { Legacy = role.HasValue ? false : (bool?)null };
                return role.HasValue ? ReplaceLocked(connectionId, role.Value, legacy: false) : Array.Empty<string>();
            }
        }

        /// <summary>The connection's role; null for an unknown connection or one not classified yet.</summary>
        public ClientRole? GetRole(string connectionId)
        {
            lock (_gate)
                return _connections.TryGetValue(connectionId, out var entry) ? entry.Role : null;
        }

        /// <summary>The connection's plugin token; null for an unknown connection or one without a token.</summary>
        public string? GetToken(string connectionId)
        {
            lock (_gate)
                return _connections.TryGetValue(connectionId, out var entry) ? entry.Token : null;
        }

        /// <summary>
        /// Sets the role of a connection whose role was not known, returning its token and the connections
        /// it replaces, which are forgotten here: every older connection of the same role, or the connection
        /// itself when a newer one of that role is already there. False (and nothing changes) when the
        /// connection is gone or already has a role: a declared role wins over the handshake.
        /// </summary>
        public bool TryClassify(string connectionId, ClientRole role, out string? token, out IReadOnlyList<string> replaced)
            => TryClassify(connectionId, role, legacy: false, out token, out replaced);

        /// <summary>
        /// The classification of a plugin that is <paramref name="legacy"/> (built before McpPlugin 8.14.0) or not.
        /// Between a legacy connection and any other, the newest replaces the older whatever their roles (server
        /// 9.2.x's newest-wins); between 8.14+ plugins only the same role is replaced.
        /// </summary>
        public bool TryClassify(string connectionId, ClientRole role, bool legacy, out string? token, out IReadOnlyList<string> replaced)
        {
            token = null;
            replaced = Array.Empty<string>();
            lock (_gate)
            {
                if (!_connections.TryGetValue(connectionId, out var entry) || entry.Role.HasValue)
                    return false;
                entry.Role = role;
                entry.Legacy = legacy;
                token = entry.Token;
                replaced = ReplaceLocked(connectionId, role, legacy);
                return true;
            }
        }

        IReadOnlyList<string> ReplaceLocked(string connectionId, ClientRole role, bool legacy)
        {
            var sequence = _connections[connectionId].Sequence;
            // The rivals: the same role between 8.14+ plugins; every classified connection when either side is legacy.
            var others = _connections.Where(pair => pair.Key != connectionId && pair.Value.Role.HasValue
                && (legacy || pair.Value.Legacy == true || pair.Value.Role == role)).ToList();
            var replaced = others.Any(pair => pair.Value.Sequence > sequence)
                ? new List<string> { connectionId } // a newer rival is already registered
                : others.Select(pair => pair.Key).ToList();
            foreach (var id in replaced)
                _connections.Remove(id);
            return replaced;
        }

        /// <summary>
        /// Forgets a connection and returns whether it was registered and its role. Remembers when the last
        /// editor left (see <see cref="EditorLeftWithin"/>).
        /// </summary>
        public bool Remove(string connectionId, out ClientRole? role)
        {
            lock (_gate)
            {
                if (!_connections.Remove(connectionId, out var entry))
                {
                    role = null;
                    return false;
                }
                role = entry.Role;
                if (role == ClientRole.Editor)
                    _lastEditorDisconnect = _clock();
                return true;
            }
        }

        /// <summary>The connections with <paramref name="role"/>, newest first.</summary>
        public IReadOnlyList<string> WithRole(ClientRole role)
        {
            lock (_gate)
                return _connections
                    .Where(pair => pair.Value.Role == role)
                    .OrderByDescending(pair => pair.Value.Sequence)
                    .Select(pair => pair.Key)
                    .ToList();
        }

        /// <summary>True when an editor disconnected less than <paramref name="window"/> ago.</summary>
        public bool EditorLeftWithin(TimeSpan window)
        {
            lock (_gate)
                return _lastEditorDisconnect.HasValue && _clock() - _lastEditorDisconnect.Value < window;
        }

        sealed class Entry
        {
            public Entry(ClientRole? role, string? token, long sequence)
            {
                Role = role;
                Token = token;
                Sequence = sequence;
            }

            public ClientRole? Role { get; set; }
            /// <summary>True for a plugin before McpPlugin 8.14.0; null while not known (no role yet).</summary>
            public bool? Legacy { get; set; }
            public string? Token { get; }
            public long Sequence { get; }
        }
    }
}
