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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Runs each request id once. A request whose id is running, or finished less than
    /// <see cref="Retention"/> ago, is not run again: the caller gets the first run's task. Defense in
    /// depth against a server that re-sends a tool call (older McpPlugin.Server versions re-sent a
    /// tool call that was slower than <c>plugin-timeout</c>, so a slow tool ran up to ten times).
    /// A request without an id always runs.
    /// </summary>
    internal sealed class DuplicateRequestGuard<T>
    {
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(10);

        readonly ConcurrentDictionary<string, Entry> _entries = new();
        readonly ILogger? _logger;

        public TimeSpan Retention { get; }

        public DuplicateRequestGuard(ILogger? logger = null, TimeSpan? retention = null)
        {
            _logger = logger;
            Retention = retention ?? DefaultRetention;
        }

        public Task<T> Run(string? requestId, Func<Task<T>> run)
        {
            if (string.IsNullOrEmpty(requestId))
                return run();

            RemoveExpired();

            var entry = new Entry(run);
            var existing = _entries.GetOrAdd(requestId!, entry);
            if (existing != entry)
            {
                _logger?.LogWarning("Request '{requestId}' arrived again while it is running or just finished. " +
                    "Not running it a second time; answering with the first run's result.", requestId);
                return existing.Task.Value;
            }
            return entry.Task.Value;
        }

        void RemoveExpired()
        {
            var now = Stopwatch.GetTimestamp();
            var retentionTicks = (long)(Retention.TotalSeconds * Stopwatch.Frequency);
            foreach (var pair in _entries)
            {
                var completedAt = pair.Value.CompletedAt;
                if (completedAt != 0 && now - completedAt >= retentionTicks)
                    _entries.TryRemove(pair.Key, out _);
            }
        }

        sealed class Entry
        {
            long _completedAt;

            public Lazy<Task<T>> Task { get; }

            /// <summary>Stopwatch timestamp of completion; 0 while running.</summary>
            public long CompletedAt => System.Threading.Interlocked.Read(ref _completedAt);

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
                    System.Threading.Interlocked.Exchange(ref _completedAt, Math.Max(1, Stopwatch.GetTimestamp()));
                }
            }
        }
    }
}
