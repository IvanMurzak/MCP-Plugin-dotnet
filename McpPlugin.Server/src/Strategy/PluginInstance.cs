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

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// Immutable instance metadata sent by an engine plugin in the hub handshake (mcp-authorize b3,
    /// design doc 04). The wire format the plugin uses to deliver these fields is refined in b7; the
    /// server-side contract is defined here.
    /// </summary>
    public sealed record PluginInstanceMetadata(
        string InstanceId,       // GUID minted per editor session (engine-side)
        string Engine,           // "unity" | "godot" | "unreal"
        string ProjectName,      // e.g. "MyGame"
        string ProjectPathHash,  // v2 hash: stable id across editor restarts (sha256 hex of v2-normalized path)
        string MachineName,
        // Dual-hash transition (auth-fixes T3 / defect B5): the v1 (legacy) hash of the same path,
        // sent alongside the v2 hash so a session pinned by an OLD (v1-pin) config still matches. May
        // be empty for a pre-dual-hash plugin (then only the v2 hash is pin-matchable).
        string ProjectPathHashLegacy = "");

    /// <summary>
    /// A single live engine-plugin connection within one account's bucket (mcp-authorize b3). Identity
    /// fields are immutable; <see cref="ConnectionId"/> is replaced on reconnect (no orphaning) and
    /// <see cref="LastActiveAt"/> is bumped on every routed request/notification. All mutations are
    /// serialized by <see cref="AccountInstances"/> under its per-account lock.
    /// </summary>
    public sealed class PluginInstance
    {
        long _lastActiveAtUtcTicks;
        string _connectionId;

        /// <summary>GUID minted per editor session (engine-side). Stable across reconnects of the same editor.</summary>
        public string InstanceId { get; }

        /// <summary>"unity" | "godot" | "unreal".</summary>
        public string Engine { get; }

        /// <summary>Human-facing project name, e.g. "MyGame".</summary>
        public string ProjectName { get; }

        /// <summary>SHA-256 hex of the v2-normalized project path — stable across editor restarts. Pin-matched by prefix.</summary>
        public string ProjectPathHash { get; }

        /// <summary>
        /// SHA-256 hex of the v1 (legacy)-normalized project path — sent alongside <see cref="ProjectPathHash"/>
        /// so a session pinned by an OLD (v1-pin) config still matches (dual-hash transition, auth-fixes
        /// T3 / defect B5). Empty for a pre-dual-hash plugin; never matches a pin when empty.
        /// </summary>
        public string ProjectPathHashLegacy { get; }

        /// <summary>The engine plugin's host machine name.</summary>
        public string MachineName { get; }

        /// <summary>When this instance first connected (never updated on reconnect-replace).</summary>
        public DateTimeOffset ConnectedAt { get; }

        /// <summary>The live SignalR connection id. Replaced on reconnect with the same InstanceId.</summary>
        public string ConnectionId => Volatile.Read(ref _connectionId);

        /// <summary>Bumped on every routed request/notification; drives MRU selection.</summary>
        public DateTimeOffset LastActiveAt
            => new DateTimeOffset(Volatile.Read(ref _lastActiveAtUtcTicks), TimeSpan.Zero);

        public PluginInstance(PluginInstanceMetadata metadata, string connectionId, DateTimeOffset connectedAt)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrEmpty(metadata.InstanceId)) throw new ArgumentException("InstanceId must be non-empty.", nameof(metadata));
            if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException("ConnectionId must be non-empty.", nameof(connectionId));

            InstanceId = metadata.InstanceId;
            Engine = metadata.Engine ?? string.Empty;
            ProjectName = metadata.ProjectName ?? string.Empty;
            ProjectPathHash = metadata.ProjectPathHash ?? string.Empty;
            ProjectPathHashLegacy = metadata.ProjectPathHashLegacy ?? string.Empty;
            MachineName = metadata.MachineName ?? string.Empty;
            ConnectedAt = connectedAt;
            _connectionId = connectionId;
            _lastActiveAtUtcTicks = connectedAt.UtcDateTime.Ticks;
        }

        /// <summary>Replace the live connection (reconnect with the same InstanceId). Serialized by the registry.</summary>
        internal void ReplaceConnection(string connectionId, DateTimeOffset at)
        {
            Volatile.Write(ref _connectionId, connectionId);
            Volatile.Write(ref _lastActiveAtUtcTicks, at.UtcDateTime.Ticks);
        }

        /// <summary>Bump the activity timestamp (drives MRU). Monotonic — never moves backwards.</summary>
        internal void BumpLastActive(DateTimeOffset at)
        {
            var ticks = at.UtcDateTime.Ticks;
            long current;
            do
            {
                current = Volatile.Read(ref _lastActiveAtUtcTicks);
                if (ticks <= current)
                    return;
            }
            while (Interlocked.CompareExchange(ref _lastActiveAtUtcTicks, ticks, current) != current);
        }

        /// <summary>
        /// True when a session pin routes to this instance: the pin is the first 8 hex chars of the
        /// project's SHA-256, so it matches when it is a case-insensitive prefix of the full
        /// project-path hash. Dual-hash transition (auth-fixes T3 / defect B5): the pin matches when it
        /// is a prefix of EITHER the v2 <see cref="ProjectPathHash"/> (new configs) OR the v1
        /// <see cref="ProjectPathHashLegacy"/> (old configs), so an old <c>.mcp.json</c> keeps routing
        /// to a new plugin. A dedup key ties an instance to its (path,engine,machine).
        /// </summary>
        public bool MatchesPin(string? pin)
        {
            if (string.IsNullOrEmpty(pin))
                return false;
            string prefix = pin; // narrowed to non-null/non-empty by the guard above
            // The pin matches when it prefixes EITHER hash (v2 for new configs, v1 legacy for old ones).
            return IsPrefixedBy(ProjectPathHash) || IsPrefixedBy(ProjectPathHashLegacy);

            bool IsPrefixedBy(string hash) =>
                !string.IsNullOrEmpty(hash) && hash.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The dedup key: same physical editor project re-launched (new InstanceId) collides on this.</summary>
        public string DedupKey => DedupKeyOf(ProjectPathHash, Engine, MachineName);

        internal static string DedupKeyOf(string projectPathHash, string engine, string machineName)
            => $"{projectPathHash}\0{engine}\0{machineName}".ToLowerInvariant();
    }
}
