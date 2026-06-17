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
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Shouldly;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using R3;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// Tests for the server-side rejection detection logic in ConnectionManager.
    /// Uses testable subclasses that override AttemptConnection and WaitBeforeRetry
    /// to simulate server behavior without requiring a real SignalR server.
    /// </summary>
    public class ConnectionManagerRejectionTests
    {
        private readonly ILogger _logger;
        private readonly Mock<IHubConnectionProvider> _mockProvider;
        private readonly Common.Version _testVersion;
        private readonly string _testEndpoint;

        public ConnectionManagerRejectionTests(ITestOutputHelper output)
        {
            var loggerFactory = TestLoggerFactory.Create(output, LogLevel.Trace);
            _logger = loggerFactory.CreateLogger<ConnectionManagerRejectionTests>();
            _mockProvider = new Mock<IHubConnectionProvider>();
            _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };
            _testEndpoint = "http://localhost:5000/hub";

            _mockProvider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(CreateDummyHubConnection());
        }

        [Fact]
        public async Task Connect_StopsAfterConsecutiveRejections()
        {
            // Arrange: every AttemptConnection "succeeds" but the connection state stays Disconnected
            // (simulates server accepting handshake then closing for auth failure).
            await using var cm = new RejectingConnectionManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await cm.Connect(cts.Token);

            // Assert
            result.ShouldBeFalse("Connection should fail after repeated rejections");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("KeepConnected should be disabled after rejection threshold");
            cm.AttemptCount.ShouldBe(3, "Should have attempted exactly MaxConsecutiveRejections times");
        }

        [Fact]
        public async Task Connect_FailedAttemptsResetRejectionCounter()
        {
            // Arrange: alternate between "rejected" (attempt succeeds, state stays Disconnected)
            // and "failed" (attempt returns false — server unreachable).
            // Pattern: reject, fail, reject, fail, reject, fail — never reaches 3 consecutive rejections
            // because each failure resets the counter.
            // Contrast with StopsAfterConsecutiveRejections where 3 consecutive true results
            // trigger the threshold after only 3 attempts.
            var sequence = new[] { true, false, true, false, true, false };
            await using var cm = new SequenceConnectionManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object,
                attemptResults: sequence
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await cm.Connect(cts.Token);

            // Assert: more than 3 attempts must have been consumed.
            // If rejection counter weren't being reset by failures, it would stop at 3.
            result.ShouldBeFalse("Connection should eventually fail");
            cm.AttemptCount.ShouldBeGreaterThan(3,
                "More than MaxConsecutiveRejections attempts should run — counter was reset by interleaved failures");
        }

        [Fact]
        public async Task Connect_DoesNotTriggerRejection_WhenConnectionStaysAlive()
        {
            // Arrange: AttemptConnection always fails (returns false), simulating a server
            // that can't be reached. This should NOT trigger the rejection detection,
            // because rejection requires AttemptConnection to return true (handshake success)
            // followed by an immediate disconnect.
            await using var cm = new NeverConnectsManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object
            );

            var rejectionFired = false;
            cm.OnAuthorizationRejected.Subscribe(_ => rejectionFired = true);

            // Act: use a short CTS timeout to stop the loop naturally instead of mutating
            // _continueToReconnect from within the test manager.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await cm.Connect(cts.Token);

            // Assert: Connect fails but OnAuthorizationRejected was never emitted, because
            // rejection detection only fires when AttemptConnection returns true (handshake
            // success) followed by an immediate disconnect — neither of which happened here.
            result.ShouldBeFalse("Connection should fail (server unreachable)");
            rejectionFired.ShouldBeFalse("OnAuthorizationRejected must not fire when AttemptConnection always returns false");
            cm.AttemptCount.ShouldBeGreaterThan(0, "Should have attempted at least once");
        }

        [Fact]
        public async Task Connect_StopsAfterConsecutiveConnectionFailures_WhenOptedIn()
        {
            // godotengine/godot#78513 regression: when a consumer OPTS IN via
            // ConnectionConfig.MaxConsecutiveConnectionFailures (> 0), an UNREACHABLE endpoint (AttemptConnection
            // always false) must NOT be retried forever — a perpetual reconnect keeps a fresh negotiate in-flight,
            // a hot-reload pin for a collectible-ALC host. The loop must GIVE UP on its own after the cap so the
            // connection settles into idle-Disconnected (no in-flight transport work), making reloads after it clean.
            await using var cm = new NeverConnectsManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object, maxConsecutiveConnectionFailures: 4
            );

            // A generous CTS that must NEVER fire — the loop has to terminate via the failure cap, not the token.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            result.ShouldBeFalse("Connection should fail (server unreachable)");
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must give up ON ITS OWN via the failure cap, not be stopped by the CTS timeout");
            cm.KeepConnected.CurrentValue.ShouldBeFalse(
                "KeepConnected must be disabled once the consecutive-connection-failure cap is hit");
            cm.AttemptCount.ShouldBe(4,
                "Should stop after exactly the opted-in cap (4) attempts");
        }

        [Fact]
        public async Task Connect_RetriesUnlimited_ByDefault_WhenNotOptedIn()
        {
            // DEFAULT (cap == 0) must preserve the historical behaviour for Unity/Unreal: retry an unreachable
            // endpoint FOREVER (until externally cancelled). Guards against the bounded reconnect leaking on by
            // default and silently changing reconnection semantics for non-collectible-ALC hosts.
            await using var cm = new NeverConnectsManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object   // no cap => 0 => unlimited
            );

            // The ONLY thing that can stop the loop here is the external CTS — there is no self-imposed cap.
            // 3s (not 1s) for margin: the test base retries instantly, and under xUnit parallelism the tight spin
            // can be briefly starved, so a too-short token could fire before the loop gets CPU.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await cm.Connect(cts.Token);

            result.ShouldBeFalse("Connection should fail (server unreachable)");
            cts.Token.IsCancellationRequested.ShouldBeTrue(
                "With the default (unlimited) cap the loop must run until the CTS cancels it, never giving up itself");
            // Definitive "did not self-cap" signal: the opted-in cap path disables KeepConnected; the unlimited
            // default must NOT — only external cancellation stopped it, so reconnection intent is preserved.
            cm.KeepConnected.CurrentValue.ShouldBeTrue(
                "Default (unlimited) retry must NOT disable KeepConnected on its own — only external cancellation stopped it");
            cm.AttemptCount.ShouldBeGreaterThan(4,
                "Unlimited retry must keep going PAST what the opted-in cap (4) would allow — proof of no self-cap");
        }

        private static HubConnection CreateDummyHubConnection()
        {
            return new HubConnectionBuilder()
                .WithUrl("http://localhost:9999/dummy", options =>
                {
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                    options.SkipNegotiation = true;
                })
                .Build();
        }

        #region Test Subclasses

        /// <summary>
        /// Base class for test ConnectionManagers with fast timings.
        /// </summary>
        private abstract class FastConnectionManager : ConnectionManager
        {
            protected override TimeSpan RejectionThreshold { get; } = TimeSpan.FromMilliseconds(50);

            protected FastConnectionManager(
                ILogger logger, Common.Version version, string endpoint, IHubConnectionProvider provider,
                int maxConsecutiveConnectionFailures = 0)
                : base(logger, version, endpoint, provider, maxConsecutiveConnectionFailures)
            {
            }

            protected override Task WaitBeforeRetry(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Simulates server rejection: AttemptConnection returns true (handshake succeeds)
        /// but connection state stays Disconnected (server closes immediately).
        /// </summary>
        private class RejectingConnectionManager : FastConnectionManager
        {
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            public RejectingConnectionManager(
                ILogger logger, Common.Version version, string endpoint, IHubConnectionProvider provider)
                : base(logger, version, endpoint, provider)
            {
            }

            protected override Task<bool> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                // Return true (handshake "succeeded") but don't set _connectionState to Connected.
                // StartConnectionLoop will see Disconnected after the stability delay → rejection.
                return Task.FromResult(true);
            }
        }

        /// <summary>
        /// Plays a fixed sequence of attempt results. Stops when exhausted.
        /// </summary>
        private class SequenceConnectionManager : FastConnectionManager
        {
            private readonly bool[] _results;
            private int _index;

            public int AttemptCount => _index;

            public SequenceConnectionManager(
                ILogger logger, Common.Version version, string endpoint,
                IHubConnectionProvider provider, bool[] attemptResults)
                : base(logger, version, endpoint, provider)
            {
                _results = attemptResults;
            }

            protected override Task<bool> AttemptConnection(CancellationToken cancellationToken)
            {
                var idx = Interlocked.Increment(ref _index) - 1;
                if (idx >= _results.Length)
                {
                    _continueToReconnect.Value = false;
                    return Task.FromResult(false);
                }
                return Task.FromResult(_results[idx]);
            }
        }

        /// <summary>
        /// Simulates a server that can't be reached: AttemptConnection always returns false.
        /// The loop is stopped externally via the CancellationToken passed to Connect().
        /// </summary>
        private class NeverConnectsManager : FastConnectionManager
        {
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            public NeverConnectsManager(
                ILogger logger, Common.Version version, string endpoint,
                IHubConnectionProvider provider, int maxConsecutiveConnectionFailures = 0)
                : base(logger, version, endpoint, provider, maxConsecutiveConnectionFailures)
            {
            }

            protected override Task<bool> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                return Task.FromResult(false);
            }
        }

        #endregion
    }
}
