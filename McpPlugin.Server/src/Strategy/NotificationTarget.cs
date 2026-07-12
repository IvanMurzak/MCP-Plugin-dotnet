/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// Routing decision for an MCP-client notification (connect/disconnect) emitted
    /// by <see cref="McpServerService"/>. A connection strategy decides whether the
    /// notification should target a specific plugin connection, a set of same-account
    /// plugin connections (<c>oauth</c> account routing, mcp-authorize b3), broadcast to
    /// all connected plugins, or be dropped entirely (no addressable recipient).
    /// </summary>
    public readonly struct NotificationTarget : IEquatable<NotificationTarget>
    {
        public enum TargetKind
        {
            /// <summary>No addressable recipient; the notification is suppressed.</summary>
            Drop = 0,
            /// <summary>Broadcast to every connected plugin (single-plugin invariant required).</summary>
            Broadcast = 1,
            /// <summary>Target the specific plugin connection identified by <see cref="ConnectionId"/>.</summary>
            Specific = 2,
            /// <summary>
            /// Target every connection in <see cref="ConnectionIds"/> — the live instances of a single
            /// account (oauth account routing). Cross-tenant-safe by construction: the strategy only
            /// ever populates it with ONE account's connections.
            /// </summary>
            SpecificMany = 3,
        }

        public TargetKind Kind { get; }
        public string? ConnectionId { get; }

        readonly IReadOnlyList<string>? _connectionIds;

        /// <summary>The set of target connections for <see cref="TargetKind.SpecificMany"/> (empty otherwise).</summary>
        public IReadOnlyList<string> ConnectionIds => _connectionIds ?? Array.Empty<string>();

        NotificationTarget(TargetKind kind, string? connectionId, IReadOnlyList<string>? connectionIds)
        {
            Kind = kind;
            ConnectionId = connectionId;
            _connectionIds = connectionIds;
        }

        public static NotificationTarget Drop() => new NotificationTarget(TargetKind.Drop, null, null);
        public static NotificationTarget Broadcast() => new NotificationTarget(TargetKind.Broadcast, null, null);
        public static NotificationTarget Specific(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                throw new ArgumentException("Connection id must be non-empty for a specific target.", nameof(connectionId));
            return new NotificationTarget(TargetKind.Specific, connectionId, null);
        }

        /// <summary>
        /// Target a set of same-account plugin connections. An empty/all-empty set collapses to
        /// <see cref="Drop"/>; a single entry collapses to <see cref="Specific"/>.
        /// </summary>
        public static NotificationTarget SpecificMany(IEnumerable<string> connectionIds)
        {
            var list = (connectionIds ?? Enumerable.Empty<string>())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (list.Count == 0)
                return Drop();
            if (list.Count == 1)
                return Specific(list[0]);
            return new NotificationTarget(TargetKind.SpecificMany, null, list);
        }

        public bool Equals(NotificationTarget other)
        {
            if (Kind != other.Kind || !string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal))
                return false;
            return ConnectionIds.SequenceEqual(other.ConnectionIds, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => obj is NotificationTarget other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ((int)Kind * 397) ^ (ConnectionId != null ? StringComparer.Ordinal.GetHashCode(ConnectionId) : 0);
                // Fold in the SpecificMany set so the hash stays consistent with Equals (which compares
                // ConnectionIds via SequenceEqual) — otherwise every SpecificMany target collides to one bucket.
                foreach (var id in ConnectionIds)
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(id);
                return hash;
            }
        }

        public static bool operator ==(NotificationTarget left, NotificationTarget right) => left.Equals(right);
        public static bool operator !=(NotificationTarget left, NotificationTarget right) => !left.Equals(right);
    }
}
