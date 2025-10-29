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
            try
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken));
                return completedTask == task;
            }
            catch (OperationCanceledException)
            {
                return false; // Task was cancelled
            }
        }

        public static Task<bool> WaitWithTimeout<T>(this Task<T> task, int timeoutMs = DefaultTimeoutMs, CancellationToken cancellationToken = default)
            => WaitWithTimeout(task, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);

        public static async Task<bool> WaitWithTimeout<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken));
                return completedTask == task;
            }
            catch (OperationCanceledException)
            {
                return false; // Task was cancelled
            }
        }
    }
}
