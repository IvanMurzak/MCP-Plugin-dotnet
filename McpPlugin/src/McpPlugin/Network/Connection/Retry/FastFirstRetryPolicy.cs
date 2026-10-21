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
using Microsoft.AspNetCore.SignalR.Client;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// SignalR auto-reconnect policy: the FIRST retry after a lost connection comes after a short
    /// jittered delay (<see cref="ReconnectJitter.FastReconnectMin"/>..<see cref="ReconnectJitter.FastReconnectMax"/>),
    /// then a fixed delay before each of the remaining retries (the historical 10 s × 3).
    /// A server that shuts down gracefully during a deploy is usually back (or replaced) within a
    /// second or two, so a fixed 10 s first wait only prolongs the outage; the jitter keeps every
    /// plugin from reconnecting in the same instant.
    /// </summary>
    internal sealed class FastFirstRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan _delay;
        private readonly int _laterRetries;

        public FastFirstRetryPolicy(TimeSpan delay, int laterRetries)
        {
            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), delay, "delay must be non-negative.");
            if (laterRetries < 0)
                throw new ArgumentOutOfRangeException(nameof(laterRetries), laterRetries, "laterRetries must be >= 0.");

            _delay = delay;
            _laterRetries = laterRetries;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount == 0)
                return ReconnectJitter.NextFastReconnectDelay();
            if (retryContext.PreviousRetryCount > _laterRetries)
                return null;
            return _delay;
        }
    }

    /// <summary>Jittered short delay shared by the fast reconnect paths.</summary>
    internal static class ReconnectJitter
    {
        internal static readonly TimeSpan FastReconnectMin = TimeSpan.FromMilliseconds(200);
        internal static readonly TimeSpan FastReconnectMax = TimeSpan.FromMilliseconds(800);

        private static readonly Random _random = new Random();

        internal static TimeSpan NextFastReconnectDelay()
        {
            int ms;
            lock (_random)
                ms = _random.Next((int)FastReconnectMin.TotalMilliseconds, (int)FastReconnectMax.TotalMilliseconds + 1);
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
