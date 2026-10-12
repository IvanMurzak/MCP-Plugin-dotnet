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
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ExtensionsTask
    {
        const int DefaultTimeoutMs = 60000; // Default timeout in milliseconds

        public static Task<bool> WaitWithTimeout(this Task task, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
            => WaitWithTimeout(task, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);

        public static async Task<bool> WaitWithTimeout(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCts.Token));
                // A cancelled delay also wins WhenAny immediately. It is not an elapsed timeout.
                cancellationToken.ThrowIfCancellationRequested();
                return completedTask == task;
            }
            finally
            {
                // Release the timer and caller-token registration when the invocation finishes first.
                timeoutCts.Cancel();
            }
        }

        public static Task<bool> WaitWithTimeout<T>(this Task<T> task, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
            => WaitWithTimeout(task, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);

        public static Task<bool> WaitWithTimeout<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
            => ((Task)task).WaitWithTimeout(timeout, cancellationToken);
    }
}
