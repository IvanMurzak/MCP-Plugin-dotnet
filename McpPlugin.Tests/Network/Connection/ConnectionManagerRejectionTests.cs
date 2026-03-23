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
                _logger, _testVersion, _testEndpoint, _mockProvider.Object,
                maxAttempts: 3
            );

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await cm.Connect(cts.Token);

            // Assert: Connect fails but KeepConnected is not disabled by auth rejection detection.
            // (It is set to false by NeverConnectsManager to stop the loop, but that's test infra,
            // not the rejection detection path.)
            result.ShouldBeFalse("Connection should fail (server unreachable)");
            cm.AttemptCount.ShouldBeGreaterThanOrEqualTo(3,
                "Should have attempted at least maxAttempts times");
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
                ILogger logger, Common.Version version, string endpoint, IHubConnectionProvider provider)
                : base(logger, version, endpoint, provider)
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
        /// Stops after maxAttempts to avoid infinite loop.
        /// </summary>
        private class NeverConnectsManager : FastConnectionManager
        {
            private readonly int _maxAttempts;
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            public NeverConnectsManager(
                ILogger logger, Common.Version version, string endpoint,
                IHubConnectionProvider provider, int maxAttempts)
                : base(logger, version, endpoint, provider)
            {
                _maxAttempts = maxAttempts;
            }

            protected override Task<bool> AttemptConnection(CancellationToken cancellationToken)
            {
                var count = Interlocked.Increment(ref _attemptCount);
                if (count >= _maxAttempts)
                    _continueToReconnect.Value = false;
                return Task.FromResult(false);
            }
        }

        #endregion
    }
}
