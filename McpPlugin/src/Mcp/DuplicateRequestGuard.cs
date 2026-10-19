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
using System.Diagnostics;
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
    /// to ten times). A request without an id always runs.
    /// </summary>
    internal sealed class DuplicateRequestGuard<T>
    {
        readonly ConcurrentDictionary<string, Entry> _entries = new();
        readonly ILogger? _logger;
        readonly long _retentionTicks;
        readonly long _sweepIntervalTicks;
        long _lastSweep = Stopwatch.GetTimestamp();

        public DuplicateRequestGuard(ILogger? logger = null, TimeSpan? retention = null)
        {
            _logger = logger;
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
                var entry = new Entry(run);
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
            var now = Stopwatch.GetTimestamp();
            var lastSweep = Interlocked.Read(ref _lastSweep);
            if (now - lastSweep < _sweepIntervalTicks || Interlocked.CompareExchange(ref _lastSweep, now, lastSweep) != lastSweep)
                return;

            foreach (var pair in _entries)
            {
                var completedAt = pair.Value.CompletedAt;
                if (completedAt != 0 && now - completedAt >= _retentionTicks)
                    _entries.TryRemove(pair.Key, out _);
            }
        }

        sealed class Entry
        {
            long _completedAt;

            public Lazy<Task<T>> Task { get; }

            /// <summary>Stopwatch timestamp of completion; 0 while running.</summary>
            public long CompletedAt => Interlocked.Read(ref _completedAt);

            public Entry(Func<Task<T>> run)
            {
                Task = new Lazy<Task<T>>(() => RunAndStamp(run));
            }

            async Task<T> RunAndStamp(Func<Task<T>> run)
            {
                try
                {
                    return await run().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _completedAt, Math.Max(1, Stopwatch.GetTimestamp()));
                }
            }
        }
    }
}
