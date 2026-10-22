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
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Tests.Infrastructure
{
    /// <summary>A thread-safe <see cref="ILogger"/> that records every formatted entry, for log assertions.</summary>
    public sealed class CapturingLogger : ILogger
    {
        readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel, string)>();

        /// <summary>Every warning message logged so far, in order.</summary>
        public IReadOnlyList<string> Warnings
        {
            get { lock (_entries) return _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList(); }
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }

        sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}
