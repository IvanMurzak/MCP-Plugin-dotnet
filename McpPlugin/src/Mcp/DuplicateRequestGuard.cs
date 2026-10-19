/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Runs each request id once. A request whose id is running, or finished less than the
    /// retention window ago (default 5 minutes), is not run again: the caller gets the first run's
    /// task. Defense in depth against a server that re-sends a tool call (older McpPlugin.Server
    /// versions re-sent a tool call that was slower than <c>plugin-timeout</c>, so a slow tool ran up
    /// to ten times). A request without an id always runs, and a cancelled run is forgotten so a later
    /// copy runs instead of receiving the cancellation.
    /// <para>At most <see cref="DefaultMaxCompletedEntries"/> finished results are kept (oldest go first;
    /// running entries are never evicted). The guard lives in memory, so a host reload (e.g. a Unity
    /// domain reload triggered by the tool itself) forgets it: an older server's re-send after the
    /// reconnect can still run the tool again.</para>
    /// </summary>
    internal sealed class DuplicateRequestGuard<T>
    {
        public const int DefaultMaxCompletedEntries = 256;

        readonly ConcurrentDictionary<string, Entry> _entries = new();
        readonly ILogger? _logger;
        readonly Func<long> _clock;
        readonly long _retentionTicks;
        readonly long _sweepIntervalTicks;
        readonly int _maxCompletedEntries;
        long _lastSweep;

        /// <param name="clock">Timestamp source in <see cref="Stopwatch.Frequency"/> ticks; tests inject one.</param>
        public DuplicateRequestGuard(ILogger? logger = null, TimeSpan? retention = null,
            int maxCompletedEntries = DefaultMaxCompletedEntries, Func<long>? clock = null)
        {
            _logger = logger;
            _clock = clock ?? Stopwatch.GetTimestamp;
            _lastSweep = _clock();
            _maxCompletedEntries = Math.Max(1, maxCompletedEntries);
            var retentionSeconds = (retention ?? TimeSpan.FromMinutes(5)).TotalSeconds;
            _retentionTicks = (long)(retentionSeconds * Stopwatch.Frequency);
            // Sweep at most a few times per window, not on every call.
            _sweepIntervalTicks = _retentionTicks / 4;
        }

        public Task<T> Run(string? requestId, Func<Task<T>> run)
        {
            if (string.IsNullOrEmpty(requestId))
                return run();

            RemoveExpired();

            if (!_entries.TryGetValue(requestId!, out var existing))
            {
                var entry = new Entry(this, requestId!, run);
                existing = _entries.GetOrAdd(requestId!, entry);
                if (existing == entry)
                    return entry.Task.Value;
            }
            _logger?.LogWarning("Request '{requestId}' arrived again while it is running or just finished. " +
                "Not running it a second time; answering with the first run's result.", requestId);
            return existing.Task.Value;
        }

        void RemoveExpired()
        {
            var now = _clock();
            var lastSweep = Interlocked.Read(ref _lastSweep);
            if (now - lastSweep < _sweepIntervalTicks || Interlocked.CompareExchange(ref _lastSweep, now, lastSweep) != lastSweep)
                return;

            foreach (var pair in _entries)
            {
                var completedAt = pair.Value.CompletedAt;
                if (completedAt != 0 && now - completedAt >= _retentionTicks)
                    Remove(pair.Key, pair.Value);
            }
        }

        /// <summary>Keeps at most <see cref="_maxCompletedEntries"/> finished entries, dropping the oldest.</summary>
        void EvictOverflow()
        {
            if (_entries.Count <= _maxCompletedEntries)
                return;

            var completed = _entries.Where(static pair => pair.Value.CompletedAt != 0).ToList();
            var excess = completed.Count - _maxCompletedEntries;
            if (excess <= 0)
                return;

            foreach (var pair in completed.OrderBy(static pair => pair.Value.CompletedAt).Take(excess))
                Remove(pair.Key, pair.Value);
        }

        /// <summary>Removes the entry only if the key still maps to it.</summary>
        void Remove(string requestId, Entry entry)
            => ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(requestId, entry));

        sealed class Entry
        {
            readonly DuplicateRequestGuard<T> _owner;
            readonly string _requestId;
            long _completedAt;

            public Lazy<Task<T>> Task { get; }

            /// <summary>Clock timestamp of completion; 0 while running.</summary>
            public long CompletedAt => Interlocked.Read(ref _completedAt);

            public Entry(DuplicateRequestGuard<T> owner, string requestId, Func<Task<T>> run)
            {
                _owner = owner;
                _requestId = requestId;
                Task = new Lazy<Task<T>>(() => RunAndStamp(run));
            }

            async Task<T> RunAndStamp(Func<Task<T>> run)
            {
                try
                {
                    return await run().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Nobody got an answer: forget the run so a later copy runs instead of being cancelled too.
                    _owner.Remove(_requestId, this);
                    throw;
                }
                finally
                {
                    Interlocked.Exchange(ref _completedAt, Math.Max(1, _owner._clock()));
                    _owner.EvictOverflow();
                }
            }
        }
    }
}
