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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// After a graceful server-initiated close (the server shutting down for a deploy), the plugin
    /// must come back quickly: the fresh connect cycle starts after a short jittered delay (≤ 1 s)
    /// instead of the fixed stabilization delay, and SignalR's own auto-reconnect makes its first
    /// retry quickly too. Later backoff is unchanged.
    /// </summary>
    public class ConnectionManagerServerCloseReconnectTests
    {
        private readonly ILogger _logger;
        private readonly Mock<IHubConnectionProvider> _mockProvider;
        private readonly Common.Version _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };

        public ConnectionManagerServerCloseReconnectTests(ITestOutputHelper output)
        {
            _logger = TestLoggerFactory.Create(output, LogLevel.Trace).CreateLogger<ConnectionManagerServerCloseReconnectTests>();
            _mockProvider = new Mock<IHubConnectionProvider>();
            _mockProvider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new HubConnectionBuilder()
                    .WithUrl("http://localhost:9999/dummy", options =>
                    {
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                        options.SkipNegotiation = true;
                    })
                    .Build());
        }

        [Fact]
        public async Task GracefulServerClose_FirstAttemptStartsAfterAShortJitteredDelay()
        {
            await using var cm = new RecordingConnectionManager(_logger, _testVersion, _mockProvider.Object);

            cm.HandleHubClosed(exception: null);
            var delay = await cm.FirstAttemptDelay.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cm.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            delay.ShouldBeGreaterThanOrEqualTo(ReconnectJitter.FastReconnectMin);
            delay.ShouldBeLessThanOrEqualTo(ReconnectJitter.FastReconnectMax);
            delay.ShouldBeLessThan(TimeSpan.FromSeconds(1), "faster than the regular stabilization delay");
        }

        [Fact]
        public async Task CloseWithError_And_PlainConnect_KeepTheRegularStabilizationDelay()
        {
            await using (var cm = new RecordingConnectionManager(_logger, _testVersion, _mockProvider.Object))
            {
                cm.HandleHubClosed(new Exception("auto-reconnect exhausted"));
                (await cm.FirstAttemptDelay.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(TimeSpan.FromSeconds(1));
            }

            await using (var cm = new RecordingConnectionManager(_logger, _testVersion, _mockProvider.Object))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await cm.Connect(cts.Token);
                (await cm.FirstAttemptDelay.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(TimeSpan.FromSeconds(1));
            }
        }

        [Fact]
        public void FastReconnectDelay_IsJitteredWithinBounds()
        {
            var seen = new ConcurrentDictionary<TimeSpan, bool>();
            for (var i = 0; i < 200; i++)
            {
                var delay = ReconnectJitter.NextFastReconnectDelay();
                delay.ShouldBeGreaterThanOrEqualTo(ReconnectJitter.FastReconnectMin);
                delay.ShouldBeLessThanOrEqualTo(ReconnectJitter.FastReconnectMax);
                seen[delay] = true;
            }
            ReconnectJitter.FastReconnectMax.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(1));
            seen.Count.ShouldBeGreaterThan(1, "the delay must be jittered, not a fixed value");
        }

        [Fact]
        public void FastFirstRetryPolicy_FirstRetryIsQuick_ThenTheFixedDelay_ThenGivesUp()
        {
            var policy = new FastFirstRetryPolicy(TimeSpan.FromSeconds(10), laterRetries: 3);

            var first = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 });
            first.ShouldNotBeNull();
            first!.Value.ShouldBeLessThanOrEqualTo(ReconnectJitter.FastReconnectMax);

            for (var previous = 1; previous <= 3; previous++)
                policy.NextRetryDelay(new RetryContext { PreviousRetryCount = previous }).ShouldBe(TimeSpan.FromSeconds(10));

            policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 4 }).ShouldBeNull();
        }

        /// <summary>Records the first-attempt delay and stops after the first attempt.</summary>
        private sealed class RecordingConnectionManager : ConnectionManager
        {
            public TaskCompletionSource<TimeSpan> FirstAttemptDelay { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Attempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public RecordingConnectionManager(ILogger logger, Common.Version version, IHubConnectionProvider provider)
                : base(logger, version, "http://localhost:5000/hub", provider)
            {
            }

            internal override TimeSpan TakeFirstAttemptDelay()
            {
                var delay = base.TakeFirstAttemptDelay();
                FirstAttemptDelay.TrySetResult(delay);
                return delay;
            }

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                _continueToReconnect.Value = false; // one attempt is all these tests need
                Attempted.TrySetResult(true);
                return Task.FromResult(ConnectionAttemptResult.Failed);
            }

            protected override Task WaitBeforeRetry(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
