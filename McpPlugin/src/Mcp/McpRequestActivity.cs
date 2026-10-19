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
using System.Diagnostics;
using System.Threading;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Thread-safe record of request activity, meant for idle timers (for example an editor that quits itself
    /// after N idle seconds). <see cref="McpManager"/> counts every tool call, system tool call, prompt get and
    /// resource read that the MCP server sends to this plugin, from the moment it arrives until its response is
    /// ready. List requests are not counted: the server issues them on its own.
    /// <para>
    /// An idle timer should treat <see cref="InFlightCount"/> &gt; 0 as busy and compare <see cref="IdleDuration"/>
    /// with its timeout. A tool that answers <c>Processing</c> and finishes later is no longer in flight once that
    /// first response is sent; wrap the rest of its work in <see cref="BeginRequest"/> to keep it visible.
    /// </para>
    /// </summary>
    public sealed class McpRequestActivity
    {
        int _inFlightCount;
        long _lastActivityTimestamp = Stopwatch.GetTimestamp();

        /// <summary>Number of requests running right now.</summary>
        public int InFlightCount => Volatile.Read(ref _inFlightCount);

        /// <summary>
        /// Time since the last request started or finished (or since this object was created, if none has run);
        /// <see cref="TimeSpan.Zero"/> while any request is in flight. Measured on a monotonic clock, so a change
        /// of the system clock neither shortens nor stretches it.
        /// </summary>
        public TimeSpan IdleDuration
        {
            get
            {
                if (InFlightCount > 0)
                    return TimeSpan.Zero;
                var elapsed = Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastActivityTimestamp);
                return elapsed <= 0
                    ? TimeSpan.Zero
                    : TimeSpan.FromTicks((long)(elapsed * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
            }
        }

        /// <summary>
        /// Marks the start of a request and returns the scope that ends it. Disposing the scope more than once
        /// ends the request only once. Use it for long work that runs outside the counted call, such as the rest
        /// of a tool that answered <c>Processing</c>; always dispose the scope, or the plugin never looks idle.
        /// </summary>
        public IDisposable BeginRequest()
        {
            Interlocked.Increment(ref _inFlightCount);
            Touch();
            return new Scope(this);
        }

        void EndRequest()
        {
            // Stamp before the decrement: a reader that sees the count drop to zero then reads the finish time.
            Touch();
            Interlocked.Decrement(ref _inFlightCount);
        }

        void Touch() => Interlocked.Exchange(ref _lastActivityTimestamp, Stopwatch.GetTimestamp());

        sealed class Scope : IDisposable
        {
            McpRequestActivity? _owner;

            public Scope(McpRequestActivity owner) => _owner = owner;

            public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndRequest();
        }
    }
}
