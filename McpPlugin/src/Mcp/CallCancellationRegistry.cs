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
    /// <c>CancelCall</c> can stop it. The call sees it as <see cref="ToolCallCancellation.Token"/> and decides itself
    /// where it is safe to stop: nothing here abandons or aborts a running tool.
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
        /// Runs a call with <see cref="ToolCallCancellation.Token"/> set to its own token, linked to
        /// <paramref name="callerToken"/>. A call without a request id cannot be cancelled by id and sees
        /// <paramref name="callerToken"/> only.
        /// <para>
        /// The call starts on a thread-pool thread, not on the caller's: the SignalR client runs a handler on its
        /// invocation loop until the handler's first await, so a synchronous tool (one that blocks waiting for the
        /// engine's main thread) would otherwise hold the loop, and the <c>CancelCall</c> queued behind it would
        /// arrive only after the tool had finished.
        /// </para>
        /// </summary>
        public async Task<ResponseData<ResponseCallTool>> Run(string? requestId, CancellationToken callerToken,
            Func<Task<ResponseData<ResponseCallTool>>> run)
        {
            if (string.IsNullOrEmpty(requestId))
                return await ToolCallCancellation.Run(callerToken, run).ConfigureAwait(false);

            RemoveExpired();
            var entry = new Entry(callerToken);
            // A request id runs once (DuplicateRequestGuard), so a previous entry here is a stale Processing one.
            _entries[requestId!] = entry;
            var keep = false;
            try
            {
                var token = entry.Source.Token;
                var result = await Task.Run(() => ToolCallCancellation.Run(token, run)).ConfigureAwait(false);
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

        /// <summary>
        /// Cancels the call with this request id; an unknown or finished id is ignored. The token's callbacks run on
        /// a thread-pool thread, never on the caller's (the SignalR invocation loop).
        /// </summary>
        public void Cancel(string? requestId)
        {
            if (string.IsNullOrEmpty(requestId) || !_entries.TryGetValue(requestId!, out var entry))
            {
                _logger?.LogDebug("Cancel for request '{requestId}': no such running call.", requestId);
                return;
            }
            _logger?.LogInformation("Request '{requestId}': the caller went away; cancelling the call.", requestId);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    entry.Source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The call finished meanwhile.
                }
                catch (Exception ex)
                {
                    // A callback registered by the tool threw. The token is cancelled anyway.
                    _logger?.LogWarning(ex, "Request '{requestId}': a cancellation callback failed.", requestId);
                }
            });
        }

        /// <summary>A call that answered Processing reported its result: it can no longer be cancelled.</summary>
        public void Complete(string? requestId)
        {
            if (!string.IsNullOrEmpty(requestId) && _entries.TryRemove(requestId!, out var entry))
                entry.Dispose();
        }

        void Remove(string requestId, Entry entry)
        {
            if (((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(new KeyValuePair<string, Entry>(requestId, entry)))
                entry.Dispose();
        }

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

        sealed class Entry : IDisposable
        {
            public readonly CancellationTokenSource Source;
            readonly bool _linked;
            /// <summary>When the call answered Processing; 0 while its handler runs.</summary>
            public long ProcessingSince;

            public Entry(CancellationToken callerToken)
            {
                _linked = callerToken.CanBeCanceled;
                Source = _linked
                    ? CancellationTokenSource.CreateLinkedTokenSource(callerToken)
                    : new CancellationTokenSource();
            }

            /// <summary>
            /// Unregisters a linked source from the caller's token. A plain source holds nothing and is left alone,
            /// so a tool that still holds its token after its call ended can keep reading it.
            /// </summary>
            public void Dispose()
            {
                if (_linked)
                    Source.Dispose();
            }
        }
    }
}
