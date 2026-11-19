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
using System.Linq;
using System.Threading;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The cancellation token of the tool or system tool call that runs on the current async flow: it fires when
    /// the server reports that the caller went away (the HTTP request was aborted, or the MCP client cancelled).
    /// <see cref="CancellationToken.None"/> outside a call, and with a server that does not send cancellations.
    /// <para>
    /// Read it on the thread that runs the tool, before handing work to another thread: an engine's main-thread
    /// dispatcher captures it when a work item is queued and skips the item when it fired before the item started.
    /// Honour it only at safe points (before work starts, between steps); never stop work halfway through a write.
    /// </para>
    /// </summary>
    public static class ToolCallCancellation
    {
        static readonly AsyncLocal<CancellationToken> _token = new AsyncLocal<CancellationToken>();

        /// <summary>The current call's token; <see cref="CancellationToken.None"/> outside a call.</summary>
        public static CancellationToken Token => _token.Value;

        /// <summary>
        /// Runs <paramref name="body"/> as the body of a tool call whose token is <paramref name="token"/>: it, and every
        /// task it starts or awaits, reads <paramref name="token"/> as <see cref="Token"/>. The previous value comes back
        /// when the body returns (for an async body: when it returns its task), so the token never leaks into the
        /// caller's flow. The plugin runs every tool call this way; engine plugins and their tests use it to run code
        /// as if inside a call, e.g. to check that a main-thread dispatcher skips work whose call was cancelled.
        /// </summary>
        /// <example><code>
        /// using var cts = new CancellationTokenSource();
        /// cts.Cancel();
        /// var work = ToolCallCancellation.RunWith(cts.Token, () => MainThread.Instance.RunAsync(() => 1));
        /// // work.IsCanceled: the dispatcher saw the call's token fired and never ran the item
        /// </code></example>
        public static T RunWith<T>(CancellationToken token, Func<T> body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            var previous = _token.Value;
            _token.Value = token;
            try
            {
                return body();
            }
            finally
            {
                _token.Value = previous;
            }
        }

        /// <inheritdoc cref="RunWith{T}(CancellationToken, Func{T})"/>
        public static void RunWith(CancellationToken token, Action body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            RunWith(token, () =>
            {
                body();
                return true;
            });
        }

        /// <summary>
        /// True when <paramref name="exception"/> is the current call stopping because its caller went away: an
        /// <see cref="OperationCanceledException"/> (reflection wrappers looked through; an aggregate counts when every
        /// exception in it is one, as from a wait on several cancelled tasks) while <see cref="Token"/> has fired.
        /// Such a call is not a fault.
        /// </summary>
        internal static bool IsCallCancelled(Exception exception)
            => Token.IsCancellationRequested && IsCancellation(RunTool.Unwrap(exception));

        static bool IsCancellation(Exception exception)
            => exception is OperationCanceledException
                || exception is AggregateException aggregate
                    && aggregate.InnerExceptions.Count > 0
                    && aggregate.InnerExceptions.All(inner => IsCancellation(RunTool.Unwrap(inner)));

        /// <summary>The answer of a call that <see cref="IsCallCancelled">stopped because its caller went away</see>; logged as information.</summary>
        internal static ResponseCallTool Cancelled(ILogger? logger, string? tool)
        {
            logger?.LogInformation("Tool '{tool}' stopped: its caller cancelled the call.", tool);
            return ResponseCallTool.Cancelled($"Tool '{tool}' was cancelled: the caller went away.");
        }
    }
}
