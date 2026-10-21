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
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>The plugin lists a <see cref="PluginCatalogCache"/> remembers.</summary>
    public enum PluginCatalogKind
    {
        Tools = 0,
        Prompts = 1,
        Resources = 2,
        ResourceTemplates = 3,
    }

    /// <summary>
    /// Remembers the last lists (tools, prompts, resources) each engine plugin answered, so a plugin
    /// that is briefly offline (a Unity recompile, a server deploy, an auth refresh) does not shrink an
    /// agent's tool list to the server's own tools. An agent that lists tools during that window keeps
    /// the full list, and so does not depend on a <c>tools/list_changed</c> notification it may never
    /// receive (an MCP client that closed its GET stream misses it).
    ///
    /// <para><b>Routing target.</b> An entry is keyed by what a session routes to: the account and
    /// instance id in <c>oauth</c> mode (<see cref="AccountMcpStrategy"/>), or the one plugin slot in the
    /// single-plugin modes (<c>none</c>, <c>token</c>). A custom strategy that allows several plugin
    /// connections but is not account-scoped has no safe key, so nothing is remembered for it.</para>
    ///
    /// <para><b>Isolation.</b> A remembered list is served only when live routing finds nothing for the
    /// session, and then only from the session's OWN account bucket, chosen by the same
    /// <c>pin(strict) → sticky → single → MRU</c> rules as live routing
    /// (<see cref="AccountInstances.ResolveAmong"/>). A session with no identity in <c>oauth</c> mode
    /// never gets one.</para>
    ///
    /// <para><b>When an entry is kept.</b> Only an unexpected loss of the connection keeps an entry, for
    /// <see cref="Grace"/>. The server cannot tell a recompile from a closed project (the plugin closes
    /// the connection the same way for both), so every plugin-side close counts as unexpected and is
    /// bounded by the grace period. An entry is dropped at once when the server knows the instance is
    /// gone: another instance of the same project/engine/machine registered (an editor restart,
    /// <see cref="AccountInstances.Register"/> dedup), a newer connection replaced the plugin in a
    /// single-plugin mode, or the host calls <see cref="Forget"/> / <see cref="ForgetAccount"/> (for
    /// example after revoking an account). A rejected connection is never registered, so it never
    /// creates an entry.</para>
    /// </summary>
    public sealed class PluginCatalogCache
    {
        const int KindCount = 4;
        static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
        static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

        /// <summary>Account key of the single-plugin modes, which have no accounts.</summary>
        const string SingleAccount = "";

        readonly object _gate = new object();
        readonly Dictionary<string, Dictionary<string, Entry>> _byAccount = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
        readonly Dictionary<string, Entry> _byConnection = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly IMcpConnectionStrategy _strategy;
        readonly Func<DateTimeOffset> _clock;
        readonly ILogger? _logger;
        readonly CancellationToken _stopping;
        readonly Func<string, CancellationToken, Task<ResponseListTool[]?>>? _fetchTools;
        DateTimeOffset _nextSweep;

        /// <param name="strategy">The server's connection strategy; decides the routing target.</param>
        /// <param name="grace">How long a disconnected plugin's lists are kept. Zero disables it.</param>
        /// <param name="reconnectWait">The longest a request waits for a reconnecting plugin.</param>
        /// <param name="clock">Test seam for expiry; defaults to UTC now.</param>
        /// <param name="logger">Optional logger.</param>
        /// <param name="stopping">Cancelled when the host shuts down: waits end at once.</param>
        /// <param name="fetchTools">
        /// Asks one plugin connection for its tools; used to tell whether a plugin's tool set really
        /// changed before an agent is told so. Null disables that check (every change is forwarded).
        /// </param>
        public PluginCatalogCache(
            IMcpConnectionStrategy strategy,
            TimeSpan grace,
            TimeSpan reconnectWait,
            Func<DateTimeOffset>? clock = null,
            ILogger? logger = null,
            CancellationToken stopping = default,
            Func<string, CancellationToken, Task<ResponseListTool[]?>>? fetchTools = null)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            Grace = grace < TimeSpan.Zero ? TimeSpan.Zero : grace;
            ReconnectWait = reconnectWait < TimeSpan.Zero ? TimeSpan.Zero : reconnectWait;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _logger = logger;
            _stopping = stopping;
            _fetchTools = fetchTools;
            _nextSweep = _clock() + SweepInterval;
        }

        /// <summary>How long a disconnected plugin's lists are kept.</summary>
        public TimeSpan Grace { get; }

        /// <summary>The longest a request waits for a reconnecting plugin.</summary>
        public TimeSpan ReconnectWait { get; }

        /// <summary>False when <see cref="Grace"/> is zero: nothing outlives its connection.</summary>
        public bool Enabled => Grace > TimeSpan.Zero;

        /// <summary>Number of remembered entries for plugins that are currently disconnected.</summary>
        public int OfflineCount
        {
            get
            {
                lock (_gate)
                    return _byAccount.Values.Sum(bucket => bucket.Values.Count(e => e.ConnectionId == null));
            }
        }

        // ───────────────────────────── connection lifecycle ─────────────────────────────

        /// <summary>
        /// A plugin connection finished registering. Revives the remembered entry of the same routing
        /// target (its lists stay until the plugin answers again) and drops the entries of instances
        /// this one replaces (same project, engine and machine under a different instance id).
        /// </summary>
        public void OnPluginConnected(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId) || !TryDescribe(connectionId, out var account, out var key, out var instance))
                return;

            var revived = false;
            lock (_gate)
            {
                var bucket = GetOrAddBucket(account);
                if (instance != null)
                    DropReplacedBy(bucket, instance);

                if (bucket.TryGetValue(key, out var entry))
                {
                    revived = entry.ConnectionId == null && entry.HasAnyList;
                    if (entry.ConnectionId != null)
                        _byConnection.Remove(entry.ConnectionId);
                }
                else
                {
                    entry = new Entry(account, key);
                    bucket[key] = entry;
                }

                entry.ConnectionId = connectionId;
                entry.Instance = instance;
                _byConnection[connectionId] = entry;
            }

            if (revived)
                _logger?.LogDebug("PluginCatalogCache: plugin reconnected on {connectionId}; its remembered lists are live again.", connectionId);
        }

        /// <summary>
        /// A plugin connection closed. Keeps its lists for <see cref="Grace"/> when there are any;
        /// otherwise forgets it. A close for a connection already superseded by a newer one is a no-op.
        /// </summary>
        public void OnPluginDisconnected(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            var kept = false;
            lock (_gate)
            {
                if (!_byConnection.TryGetValue(connectionId, out var entry))
                    return;
                _byConnection.Remove(connectionId);
                entry.ConnectionId = null;

                if (Enabled && entry.HasAnyList)
                {
                    entry.DisconnectedAt = _clock();
                    kept = true;
                }
                else
                {
                    RemoveEntry(entry);
                }
                SweepIfDue(_clock());
            }

            if (kept)
                _logger?.LogDebug("PluginCatalogCache: plugin on {connectionId} disconnected; keeping its lists for {grace}.", connectionId, Grace);
        }

        /// <summary>Forgets the routing target served by <paramref name="connectionId"/> (live or not).</summary>
        public void Forget(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;
            lock (_gate)
            {
                if (_byConnection.TryGetValue(connectionId, out var entry))
                    RemoveEntry(entry);
            }
        }

        /// <summary>
        /// Forgets everything remembered for <paramref name="accountId"/>, for a host that revokes an
        /// account or signs it out. Returns how many entries were dropped.
        /// </summary>
        public int ForgetAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
                return 0;
            lock (_gate)
            {
                if (!_byAccount.TryGetValue(accountId, out var bucket))
                    return 0;
                var entries = bucket.Values.ToList();
                foreach (var entry in entries)
                    RemoveEntry(entry);
                return entries.Count;
            }
        }

        // ───────────────────────────── lists ─────────────────────────────

        /// <summary>Remembers a list the plugin on <paramref name="connectionId"/> just answered.</summary>
        public void Record(string connectionId, PluginCatalogKind kind, object list)
        {
            if (string.IsNullOrEmpty(connectionId) || list == null)
                return;
            lock (_gate)
            {
                if (_byConnection.TryGetValue(connectionId, out var entry))
                    entry.Lists[(int)kind] = list;
            }
        }

        /// <summary>Drops one remembered list of a live plugin (it told the server the list changed).</summary>
        public void Invalidate(string connectionId, PluginCatalogKind kind)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;
            lock (_gate)
            {
                if (_byConnection.TryGetValue(connectionId, out var entry))
                    entry.Lists[(int)kind] = null;
            }
        }

        /// <summary>
        /// The remembered list of the disconnected plugin the CURRENT session (ambient
        /// <see cref="McpSessionTokenContext"/>) would route to, or null. Call it only when live routing
        /// found no plugin.
        /// </summary>
        public T? GetOfflineList<T>(PluginCatalogKind kind) where T : class
        {
            lock (_gate)
                return FindOfflineForCurrentSessionLocked()?.Lists[(int)kind] as T;
        }

        /// <summary>True when the current session routes to a remembered, disconnected plugin.</summary>
        public bool HasOfflineTargetForCurrentSession()
        {
            lock (_gate)
                return FindOfflineForCurrentSessionLocked() != null;
        }

        /// <summary>
        /// Waits for the remembered plugin of the current session to come back. Returns true once
        /// <paramref name="isLive"/> reports a live route; false right away when the session has no
        /// remembered plugin, and false when <see cref="ReconnectWait"/> passes, the entry expires or
        /// is dropped, or the host is stopping.
        /// </summary>
        public async Task<bool> WaitForReconnectAsync(Func<bool> isLive, CancellationToken cancellationToken)
        {
            if (isLive == null) throw new ArgumentNullException(nameof(isLive));

            Entry? entry;
            lock (_gate)
                entry = FindOfflineForCurrentSessionLocked();
            if (entry == null)
                return false;

            var waited = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_stopping.IsCancellationRequested)
                    return false;
                if (isLive())
                    return true;
                if (waited.Elapsed >= ReconnectWait)
                    return false;
                lock (_gate)
                {
                    if (!IsTracked(entry))
                        return false;
                    if (entry.ConnectionId == null && IsExpired(entry, _clock()))
                        return false;
                }
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The plugin on <paramref name="connectionId"/> said its tools changed. When its previous tool
        /// list is known, asks it for the new one in the background and calls <paramref name="notify"/>
        /// only if the set really differs (or the check fails), so a plain reconnect does not send
        /// agents a <c>tools/list_changed</c>. Returns false when there is nothing to compare against;
        /// the caller then notifies as usual.
        /// </summary>
        public bool TryNotifyIfToolsChanged(string connectionId, Action notify)
        {
            if (notify == null) throw new ArgumentNullException(nameof(notify));
            if (_fetchTools == null || string.IsNullOrEmpty(connectionId))
                return false;

            ResponseListTool[]? known;
            lock (_gate)
            {
                if (!_byConnection.TryGetValue(connectionId, out var entry))
                    return false;
                known = entry.Lists[(int)PluginCatalogKind.Tools] as ResponseListTool[];
            }
            if (known == null)
                return false;

            _ = CompareAndNotifyAsync(connectionId, known, notify);
            return true;
        }

        async Task CompareAndNotifyAsync(string connectionId, ResponseListTool[] known, Action notify)
        {
            ResponseListTool[]? fresh = null;
            try
            {
                fresh = await _fetchTools!(connectionId, _stopping).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "PluginCatalogCache: could not fetch the tools of {connectionId}; forwarding the change.", connectionId);
            }

            try
            {
                if (fresh != null)
                {
                    Record(connectionId, PluginCatalogKind.Tools, fresh);
                    if (SameToolSet(known, fresh))
                    {
                        _logger?.LogDebug("PluginCatalogCache: tools of {connectionId} are unchanged; tools/list_changed not sent.", connectionId);
                        return;
                    }
                }
                notify();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PluginCatalogCache: error forwarding a tool change of {connectionId}.", connectionId);
            }
        }

        /// <summary>True when both lists describe the same tools (order does not matter).</summary>
        public static bool SameToolSet(IReadOnlyCollection<ResponseListTool> a, IReadOnlyCollection<ResponseListTool> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;
            try
            {
                return string.Equals(Fingerprint(a), Fingerprint(b), StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // A value the serializer cannot write (e.g. an unset JsonElement): treat as changed.
                return false;
            }
        }

        static string Fingerprint(IEnumerable<ResponseListTool> tools)
            => JsonSerializer.Serialize(tools.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray());

        // ───────────────────────────── internals ─────────────────────────────

        /// <summary>
        /// The routing target a registered connection serves. False for a connection the strategy does
        /// not route to (rejected, unregistered) and for a multi-plugin strategy with no account scope.
        /// </summary>
        bool TryDescribe(string connectionId, out string account, out string key, out PluginInstance? instance)
        {
            account = SingleAccount;
            key = string.Empty;
            instance = null;

            if (_strategy is AccountMcpStrategy accountStrategy)
            {
                var found = accountStrategy.Instances.GetInstanceByConnection(connectionId);
                var owner = accountStrategy.Instances.GetAccountByConnection(connectionId);
                if (found == null || owner == null)
                    return false;
                account = owner;
                key = found.InstanceId;
                instance = found;
                return true;
            }

            if (_strategy.AllowMultipleConnections)
                return false;

            return ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Contains(connectionId, StringComparer.Ordinal);
        }

        Entry? FindOfflineForCurrentSessionLocked()
        {
            if (!Enabled)
                return null;

            string account;
            string? pin = null;
            string? selected = null;
            var accountStrategy = _strategy as AccountMcpStrategy;
            if (accountStrategy != null)
            {
                var identity = McpSessionTokenContext.CurrentIdentity;
                if (identity == null)
                    return null; // fail closed: no identity, no account, nothing to serve
                account = identity.AccountId;
                pin = McpSessionTokenContext.CurrentProjectPin;
                selected = accountStrategy.ResolveStickySelection();
            }
            else if (_strategy.AllowMultipleConnections)
            {
                return null;
            }
            else
            {
                account = SingleAccount;
            }

            var now = _clock();
            SweepIfDue(now);
            if (!_byAccount.TryGetValue(account, out var bucket))
                return null;

            PruneExpired(bucket, now);
            var offline = bucket.Values.Where(e => e.ConnectionId == null).ToList();
            if (offline.Count == 0)
                return null;
            if (accountStrategy == null)
                return offline[0];

            var candidates = offline.Where(e => e.Instance != null).ToList();
            var resolution = AccountInstances.ResolveAmong(candidates.Select(e => e.Instance!).ToList(), pin, selected);
            if (resolution.Kind != InstanceResolutionKind.Resolved || resolution.Instance == null)
                return null;
            return candidates.FirstOrDefault(e => ReferenceEquals(e.Instance, resolution.Instance));
        }

        Dictionary<string, Entry> GetOrAddBucket(string account)
        {
            if (!_byAccount.TryGetValue(account, out var bucket))
            {
                bucket = new Dictionary<string, Entry>(StringComparer.Ordinal);
                _byAccount[account] = bucket;
            }
            return bucket;
        }

        void DropReplacedBy(Dictionary<string, Entry> bucket, PluginInstance instance)
        {
            var replaced = bucket.Values
                .Where(e => e.Instance != null
                    && !string.Equals(e.Key, instance.InstanceId, StringComparison.Ordinal)
                    && string.Equals(e.Instance.DedupKey, instance.DedupKey, StringComparison.Ordinal))
                .ToList();
            foreach (var entry in replaced)
            {
                RemoveEntry(entry);
                _logger?.LogDebug("PluginCatalogCache: instance {old} replaced by {new}; its remembered lists are dropped.", entry.Key, instance.InstanceId);
            }
        }

        bool IsTracked(Entry entry)
            => _byAccount.TryGetValue(entry.Account, out var bucket)
                && bucket.TryGetValue(entry.Key, out var current)
                && ReferenceEquals(current, entry);

        bool IsExpired(Entry entry, DateTimeOffset now) => now - entry.DisconnectedAt >= Grace;

        void PruneExpired(Dictionary<string, Entry> bucket, DateTimeOffset now)
        {
            foreach (var entry in bucket.Values.Where(e => e.ConnectionId == null && IsExpired(e, now)).ToList())
                RemoveEntry(entry);
        }

        void SweepIfDue(DateTimeOffset now)
        {
            if (now < _nextSweep)
                return;
            _nextSweep = now + SweepInterval;
            foreach (var bucket in _byAccount.Values.ToList())
                PruneExpired(bucket, now);
        }

        void RemoveEntry(Entry entry)
        {
            if (entry.ConnectionId != null)
                _byConnection.Remove(entry.ConnectionId);
            if (_byAccount.TryGetValue(entry.Account, out var bucket)
                && bucket.TryGetValue(entry.Key, out var current)
                && ReferenceEquals(current, entry))
            {
                bucket.Remove(entry.Key);
                if (bucket.Count == 0)
                    _byAccount.Remove(entry.Account);
            }
        }

        sealed class Entry
        {
            public Entry(string account, string key)
            {
                Account = account;
                Key = key;
            }

            public string Account { get; }
            public string Key { get; }
            public PluginInstance? Instance { get; set; }

            /// <summary>The live connection; null while the plugin is disconnected.</summary>
            public string? ConnectionId { get; set; }
            public DateTimeOffset DisconnectedAt { get; set; }
            public object?[] Lists { get; } = new object?[KindCount];
            public bool HasAnyList => Lists.Any(l => l != null);
        }
    }
}
