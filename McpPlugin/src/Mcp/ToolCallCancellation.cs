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
using System.Threading.Tasks;

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
        /// Runs <paramref name="body"/> with <paramref name="token"/> as <see cref="Token"/>. An async method, so the
        /// value is visible to the body and everything it awaits, and never leaks back to the caller's flow.
        /// </summary>
        internal static async Task<T> Run<T>(CancellationToken token, Func<Task<T>> body)
        {
            _token.Value = token;
            return await body().ConfigureAwait(false);
        }
    }
}
