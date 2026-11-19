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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// One cancellation token per running tool or system tool call, by request id, so the server's
    /// <c>CancelCall</c> can stop it. The token is passed to the call and set as <see cref="ToolCallCancellation.Token"/>.
    /// A call that answered <see cref="ResponseStatus.Processing"/> keeps running after its answer, so its token stays
    /// cancellable until <see cref="Complete"/> (the plugin reported the result) or <see cref="ProcessingRetention"/>.
    /// </summary>
    internal sealed class CallCancellationRegistry
    {
        /// <summary>How long a call that answered Processing stays cancellable without reporting its result.</summary>
        internal static readonly TimeSpan ProcessingRetention = TimeSpan.FromMinutes(30);

        readonly ConcurrentDictionary<string, Entry> _entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
        readonly ILogger? _logger;
        readonly Func<long> _clock;
        readonly long _retentionTicks;

        /// <param name="clock">Timestamp source in <see cref="Stopwatch.Frequency"/> ticks; tests inject one.</param>
        public CallCancellationRegistry(ILogger? logger = null, Func<long>? clock = null)
        {
            _logger = logger;
            _clock = clock ?? Stopwatch.GetTimestamp;
            _retentionTicks = (long)(ProcessingRetention.TotalSeconds * Stopwatch.Frequency);
        }

        /// <summary>The number of calls that can be cancelled now (tests).</summary>
        internal int Count => _entries.Count;

        /// <summary>
        /// Runs a call with its own token, linked to <paramref name="callerToken"/>. A call without a request id
        /// cannot be cancelled by id and runs with <paramref name="callerToken"/> only.
        /// </summary>
        public async Task<ResponseData<ResponseCallTool>> Run(string? requestId, CancellationToken callerToken,
            Func<CancellationToken, Task<ResponseData<ResponseCallTool>>> run)
        {
            if (string.IsNullOrEmpty(requestId))
                return await ToolCallCancellation.Run(callerToken, () => run(callerToken)).ConfigureAwait(false);

            RemoveExpired();
            var entry = new Entry(callerToken);
            // A request id runs once (DuplicateRequestGuard), so a previous entry here is a stale Processing one.
            _entries[requestId!] = entry;
            var keep = false;
            try
            {
                var token = entry.Source.Token;
                var result = await ToolCallCancellation.Run(token, () => run(token)).ConfigureAwait(false);
                if (result?.Status == ResponseStatus.Processing && !token.IsCancellationRequested)
                {
                    Volatile.Write(ref entry.ProcessingSince, _clock());
                    keep = true;
                }
                return result!;
            }
            finally
            {
                if (!keep)
                    Remove(requestId!, entry);
            }
        }

        /// <summary>Cancels the call with this request id. An unknown or finished id is ignored.</summary>
        public void Cancel(string? requestId)
        {
            if (string.IsNullOrEmpty(requestId) || !_entries.TryGetValue(requestId!, out var entry))
            {
                _logger?.LogDebug("Cancel for request '{requestId}': no such running call.", requestId);
                return;
            }
            _logger?.LogInformation("Request '{requestId}': the caller went away; cancelling the call.", requestId);
            try
            {
                entry.Source.Cancel();
            }
            catch (Exception ex)
            {
                // A callback registered by the tool threw. The token is cancelled anyway.
                _logger?.LogWarning(ex, "Request '{requestId}': a cancellation callback failed.", requestId);
            }
        }

        /// <summary>A call that answered Processing reported its result: it can no longer be cancelled.</summary>
        public void Complete(string? requestId)
        {
            if (!string.IsNullOrEmpty(requestId))
                _entries.TryRemove(requestId!, out _);
        }

        void Remove(string requestId, Entry entry)
            => ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(requestId, entry));

        void RemoveExpired()
        {
            var now = _clock();
            foreach (var pair in _entries)
            {
                var since = Volatile.Read(ref pair.Value.ProcessingSince);
                if (since != 0 && now - since > _retentionTicks)
                    Remove(pair.Key, pair.Value);
            }
        }

        sealed class Entry
        {
            public readonly CancellationTokenSource Source;
            /// <summary>When the call answered Processing; 0 while its handler runs.</summary>
            public long ProcessingSince;

            public Entry(CancellationToken callerToken)
            {
                Source = callerToken.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
                    : new CancellationTokenSource();
            }
        }
    }
}
