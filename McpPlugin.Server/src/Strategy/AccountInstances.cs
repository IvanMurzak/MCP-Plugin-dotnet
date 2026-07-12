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
using System.Collections.Generic;
using System.Linq;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>The outcome kind of an agent-session → instance resolution (design doc 04 step 5).</summary>
    public enum InstanceResolutionKind
    {
        /// <summary>A live plugin instance was resolved for the session.</summary>
        Resolved = 0,

        /// <summary>
        /// The session is pinned to a project and the account HAS other live instances, but none
        /// match the pin — the project's editor is closed. A pin NEVER falls through to another
        /// project. (Agent-actionable "engine for this project is not connected" variant.)
        /// </summary>
        NoMatchPinned = 1,

        /// <summary>The account has NO live instances at all. (Agent-actionable "no engine connected" variant.)</summary>
        AccountEmpty = 2,
    }

    /// <summary>
    /// Result of resolving an agent session to a plugin instance. Distinguishes the two error
    /// variants required by design 04 (pinned-no-match vs account-empty); the agent-facing error
    /// TEXT is surfaced by the built-in tools in b4.
    /// </summary>
    public readonly struct InstanceResolution
    {
        public InstanceResolutionKind Kind { get; }

        /// <summary>The resolved instance when <see cref="Kind"/> is <see cref="InstanceResolutionKind.Resolved"/>; else null.</summary>
        public PluginInstance? Instance { get; }

        /// <summary>
        /// A one-time advisory note attached to the tool result when an unpinned session with
        /// MULTIPLE instances is routed to the most-recently-active one (design 04 step 4). Null otherwise.
        /// </summary>
        public string? AdvisoryNote { get; }

        InstanceResolution(InstanceResolutionKind kind, PluginInstance? instance, string? advisoryNote)
        {
            Kind = kind;
            Instance = instance;
            AdvisoryNote = advisoryNote;
        }

        public static InstanceResolution Resolved(PluginInstance instance, string? advisoryNote = null)
            => new InstanceResolution(InstanceResolutionKind.Resolved, instance, advisoryNote);

        public static InstanceResolution NoMatchPinned() => new InstanceResolution(InstanceResolutionKind.NoMatchPinned, null, null);
        public static InstanceResolution AccountEmpty() => new InstanceResolution(InstanceResolutionKind.AccountEmpty, null, null);
    }

    /// <summary>
    /// The account+instance registry (mcp-authorize b3, design doc 04) — replaces
    /// <c>ClientUtils.TokenToConnectionId</c> for the <c>oauth</c> pairing plane. Per account, all
    /// live engine-plugin connections keyed by instance id; plus a reverse index
    /// (connection id → account/instance) for O(1) disconnect and notification scoping. Structural
    /// mutations (register / reconnect-replace / dedup / remove) are serialized under a single gate;
    /// resolution reads take lock-free snapshots (eventually consistent — acceptable for routing).
    ///
    /// <para><b>Isolation invariant:</b> every lookup is scoped by <c>accountId</c> (the <c>sub</c>);
    /// a session for one account can NEVER resolve, be notified about, or observe another account's
    /// instances. Fail closed — an unresolvable session yields an error variant, never a fallthrough.</para>
    /// </summary>
    public sealed class AccountInstances
    {
        readonly object _gate = new object();

        // accountId → (instanceId → PluginInstance)
        readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PluginInstance>> _accounts =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, PluginInstance>>(StringComparer.Ordinal);

        // connectionId → (accountId, instanceId) — reverse index for disconnect + notification scoping.
        readonly ConcurrentDictionary<string, (string AccountId, string InstanceId)> _byConnection =
            new ConcurrentDictionary<string, (string, string)>(StringComparer.Ordinal);

        readonly Func<DateTimeOffset> _clock;

        public AccountInstances() : this(() => DateTimeOffset.UtcNow) { }

        /// <summary>Testable ctor — inject a clock for deterministic MRU/last-active assertions.</summary>
        public AccountInstances(Func<DateTimeOffset> clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Number of accounts with at least one live instance.</summary>
        public int AccountCount => _accounts.Count;

        /// <summary>Number of live instances in an account (0 when the account is unknown).</summary>
        public int InstanceCount(string accountId)
            => _accounts.TryGetValue(accountId, out var bucket) ? bucket.Count : 0;

        /// <summary>Snapshot of an account's live instances (empty when the account is unknown). Never cross-account.</summary>
        public IReadOnlyList<PluginInstance> GetInstances(string? accountId)
        {
            if (string.IsNullOrEmpty(accountId) || !_accounts.TryGetValue(accountId!, out var bucket))
                return Array.Empty<PluginInstance>();
            return bucket.Values.ToList();
        }

        /// <summary>
        /// Registers (or reconnect-replaces) a plugin instance for <paramref name="accountId"/>.
        /// <list type="bullet">
        ///   <item><b>Reconnect-replace:</b> an existing entry with the same <c>InstanceId</c> keeps its
        ///   slot; only its live connection id + activity are refreshed (no orphaning of session pins).</item>
        ///   <item><b>Dedup:</b> a DIFFERENT <c>InstanceId</c> with the same
        ///   (ProjectPathHash, Engine, MachineName) — an editor restart — evicts the stale entry now
        ///   (a strict, deterministic superset of "evict after the SignalR client-timeout": the stale
        ///   editor is gone, so routing must never land on its dead connection).</item>
        /// </list>
        /// Returns the live <see cref="PluginInstance"/>.
        /// </summary>
        public PluginInstance Register(string accountId, PluginInstanceMetadata metadata, string connectionId)
        {
            if (string.IsNullOrEmpty(accountId)) throw new ArgumentException("accountId must be non-empty.", nameof(accountId));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrEmpty(metadata.InstanceId)) throw new ArgumentException("InstanceId must be non-empty.", nameof(metadata));
            if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException("connectionId must be non-empty.", nameof(connectionId));

            var now = _clock();
            lock (_gate)
            {
                var bucket = _accounts.GetOrAdd(accountId, _ => new ConcurrentDictionary<string, PluginInstance>(StringComparer.Ordinal));

                // Reconnect-replace by InstanceId.
                if (bucket.TryGetValue(metadata.InstanceId, out var existing))
                {
                    // Drop the reverse index for the instance's PRIOR connection so a late disconnect
                    // for it becomes a no-op (never evicts the reconnected instance).
                    _byConnection.TryRemove(existing.ConnectionId, out _);
                    existing.ReplaceConnection(connectionId, now);
                    _byConnection[connectionId] = (accountId, existing.InstanceId);
                    return existing;
                }

                // Dedup: same physical project+engine+machine relaunched with a new InstanceId → evict stale.
                var dedupKey = PluginInstance.DedupKeyOf(metadata.ProjectPathHash ?? string.Empty, metadata.Engine ?? string.Empty, metadata.MachineName ?? string.Empty);
                foreach (var stale in bucket.Values.Where(i => string.Equals(i.DedupKey, dedupKey, StringComparison.Ordinal)).ToList())
                {
                    if (bucket.TryRemove(stale.InstanceId, out _))
                        _byConnection.TryRemove(stale.ConnectionId, out _);
                }

                var instance = new PluginInstance(metadata, connectionId, now);
                bucket[instance.InstanceId] = instance;
                _byConnection[connectionId] = (accountId, instance.InstanceId);
                return instance;
            }
        }

        /// <summary>
        /// Removes the instance whose CURRENT connection is <paramref name="connectionId"/> (a plugin
        /// disconnect). A disconnect for a stale connection (superseded by a reconnect-replace) is a
        /// no-op. Empty account buckets are dropped (no unbounded growth). Returns the removed
        /// (accountId, instanceId), or null when nothing matched.
        /// </summary>
        public (string AccountId, string InstanceId)? RemoveByConnection(string? connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return null;

            lock (_gate)
            {
                if (!_byConnection.TryGetValue(connectionId!, out var loc))
                    return null;

                _byConnection.TryRemove(connectionId!, out _);

                if (_accounts.TryGetValue(loc.AccountId, out var bucket))
                {
                    // Only remove the instance if this connection is still its current one.
                    if (bucket.TryGetValue(loc.InstanceId, out var inst) &&
                        string.Equals(inst.ConnectionId, connectionId, StringComparison.Ordinal))
                    {
                        bucket.TryRemove(loc.InstanceId, out _);
                    }
                    if (bucket.IsEmpty)
                        _accounts.TryRemove(loc.AccountId, out _);
                }
                return loc;
            }
        }

        /// <summary>The account that owns <paramref name="connectionId"/>, or null. Used for notification scoping.</summary>
        public string? GetAccountByConnection(string? connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return null;
            return _byConnection.TryGetValue(connectionId!, out var loc) ? loc.AccountId : (string?)null;
        }

        /// <summary>Look up the live instance backing a connection (null when unknown).</summary>
        public PluginInstance? GetInstanceByConnection(string? connectionId)
        {
            if (string.IsNullOrEmpty(connectionId) || !_byConnection.TryGetValue(connectionId!, out var loc))
                return null;
            return _accounts.TryGetValue(loc.AccountId, out var bucket) && bucket.TryGetValue(loc.InstanceId, out var inst)
                ? inst
                : null;
        }

        /// <summary>Bump the last-active timestamp of the instance backing a connection (drives MRU).</summary>
        public void TouchByConnection(string? connectionId)
            => GetInstanceByConnection(connectionId)?.BumpLastActive(_clock());

        /// <summary>
        /// Resolves an agent session to a plugin instance using the design-04 precedence:
        /// <c>pin(strict) → sticky → single → MRU(unpinned)</c>. A pin (when present) restricts the
        /// candidate set to matching instances and NEVER falls through to another project. Sticky
        /// narrows within the candidate set (never overrides a pin to a different project).
        /// </summary>
        public InstanceResolution Resolve(string? accountId, string? projectPin, string? selectedInstanceId)
        {
            var all = GetInstances(accountId);
            if (all.Count == 0)
                return InstanceResolution.AccountEmpty();

            // 1. Project pin — STRICT. Restrict candidates; a pin never falls through.
            IReadOnlyList<PluginInstance> candidates;
            if (!string.IsNullOrEmpty(projectPin))
            {
                candidates = all.Where(i => i.MatchesPin(projectPin)).ToList();
                if (candidates.Count == 0)
                    return InstanceResolution.NoMatchPinned();
            }
            else
            {
                candidates = all;
            }

            // 2. Sticky selection — honored only when the selected instance is within the candidate set
            //    (so it can narrow a pin, but can never override it to a different project).
            if (!string.IsNullOrEmpty(selectedInstanceId))
            {
                var sticky = candidates.FirstOrDefault(i => string.Equals(i.InstanceId, selectedInstanceId, StringComparison.Ordinal));
                if (sticky != null)
                    return InstanceResolution.Resolved(sticky);
            }

            // 3. Single candidate — auto-pair (the overwhelmingly common case; zero-friction UX).
            if (candidates.Count == 1)
                return InstanceResolution.Resolved(candidates[0]);

            // 4. Multiple candidates, unresolved → most-recently-active + one-time advisory note.
            var mru = candidates.OrderByDescending(i => i.LastActiveAt).First();
            var note = $"Routed to {mru.Engine}:{mru.ProjectName} on {mru.MachineName}; " +
                       "use list_engine_instances / select_engine_instance to switch.";
            return InstanceResolution.Resolved(mru, note);
        }
    }
}
