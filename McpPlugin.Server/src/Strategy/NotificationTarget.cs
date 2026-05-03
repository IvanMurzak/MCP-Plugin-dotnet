/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// Routing decision for an MCP-client notification (connect/disconnect) emitted
    /// by <see cref="McpServerService"/>. A connection strategy decides whether the
    /// notification should target a specific plugin connection, broadcast to all
    /// connected plugins, or be dropped entirely (no addressable recipient).
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
        }

        public TargetKind Kind { get; }
        public string? ConnectionId { get; }

        NotificationTarget(TargetKind kind, string? connectionId)
        {
            Kind = kind;
            ConnectionId = connectionId;
        }

        public static NotificationTarget Drop() => new NotificationTarget(TargetKind.Drop, null);
        public static NotificationTarget Broadcast() => new NotificationTarget(TargetKind.Broadcast, null);
        public static NotificationTarget Specific(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                throw new ArgumentException("Connection id must be non-empty for a specific target.", nameof(connectionId));
            return new NotificationTarget(TargetKind.Specific, connectionId);
        }

        public bool Equals(NotificationTarget other)
            => Kind == other.Kind && string.Equals(ConnectionId, other.ConnectionId, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is NotificationTarget other && Equals(other);

        public override int GetHashCode()
            => unchecked(((int)Kind * 397) ^ (ConnectionId != null ? StringComparer.Ordinal.GetHashCode(ConnectionId) : 0));

        public static bool operator ==(NotificationTarget left, NotificationTarget right) => left.Equals(right);
        public static bool operator !=(NotificationTarget left, NotificationTarget right) => !left.Equals(right);
    }
}
