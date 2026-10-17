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
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// The SignalR connection-error hint is consumer-supplied (<see cref="ConnectionConfig.ConnectionErrorHint"/>):
    /// the engine-agnostic library must not ship any engine/UI wording of its own.
    /// </summary>
    public sealed class ConnectionErrorHintTests
    {
        [Fact]
        public void Default_HasNoHint_AndAppendsNothingAfterErrors()
        {
            var config = new ConnectionConfig();
            config.ConnectionErrorHint.ShouldBeNull();

            var sink = new CapturingLogger();
            LogThroughHubLoggerProvider(sink, config, LogLevel.Error, "connection failed");

            sink.Entries.Count.ShouldBe(1);
            sink.Entries[0].Message.ShouldBe("connection failed");
            foreach (var (_, message) in sink.Entries)
            {
                message.ShouldNotContain("Unity", Case.Insensitive);
                message.ShouldNotContain("AI Game Developer", Case.Insensitive);
                message.ShouldNotContain("window", Case.Insensitive);
                message.ShouldNotContain("<b>");
            }
        }

        [Theory]
        [InlineData(LogLevel.Error)]
        [InlineData(LogLevel.Critical)]
        public void CustomHint_IsForwardedAfterErrors_AtTheSameLevel(LogLevel level)
        {
            const string hint = "To stop seeing this error, stop the MCP connection.";
            var config = new ConnectionConfig { ConnectionErrorHint = hint };

            var sink = new CapturingLogger();
            LogThroughHubLoggerProvider(sink, config, level, "connection failed");

            sink.Entries.Count.ShouldBe(2);
            sink.Entries[0].ShouldBe((level, "connection failed"));
            sink.Entries[1].ShouldBe((level, hint));
        }

        [Fact]
        public void CustomHint_IsNotAppendedBelowErrorLevel()
        {
            var config = new ConnectionConfig { ConnectionErrorHint = "hint" };

            var sink = new CapturingLogger();
            LogThroughHubLoggerProvider(sink, config, LogLevel.Warning, "retrying");

            sink.Entries.ShouldHaveSingleItem().ShouldBe((LogLevel.Warning, "retrying"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankHint_AppendsNothing(string hint)
        {
            var config = new ConnectionConfig { ConnectionErrorHint = hint };

            var sink = new CapturingLogger();
            LogThroughHubLoggerProvider(sink, config, LogLevel.Error, "connection failed");

            sink.Entries.ShouldHaveSingleItem().ShouldBe((LogLevel.Error, "connection failed"));
        }

        [Fact]
        public void Hint_IsRuntimeOnly_NotSerialized()
        {
            var config = new ConnectionConfig { ConnectionErrorHint = "hint" };

            var json = JsonSerializer.Serialize(config);

            json.ShouldNotContain(nameof(ConnectionConfig.ConnectionErrorHint));
        }

        static void LogThroughHubLoggerProvider(ILogger sink, ConnectionConfig config, LogLevel level, string message)
        {
            using var provider = HubConnectionProvider.CreateForwardLoggerProvider(sink, config);
            provider.CreateLogger("Microsoft.AspNetCore.SignalR.Client.HubConnection").Log(level, message);
        }

        sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
