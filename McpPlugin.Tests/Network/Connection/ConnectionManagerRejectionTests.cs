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

            // Act: the loop terminates ON ITS OWN via the rejection cap; the CTS is a pure hang
            // guard that must NEVER fire. It needs generous headroom: the happy path already
            // spends real time (ConnectionManager's 1s post-create stabilization delay plus one
            // RejectionThreshold window per rejection), and on a loaded CI runner every timer
            // resumption can stretch by seconds — a tight budget turns runner load into a
            // truncated loop and a bogus AttemptCount failure (the documented CI flake).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            // Assert
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via the rejection cap, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should fail after repeated rejections");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("KeepConnected should be disabled after rejection threshold");
            cm.AttemptCount.ShouldBe(3, "Should have attempted exactly MaxConsecutiveRejections times");
        }

        [Fact]
        public async Task Connect_FailedAttemptsResetRejectionCounter()
        {
            // PROTECTIVE INTENT (kept through the C3.4 seam change): NON-AUTH failures reset the
            // rejection counter. Alternate between "rejected" (attempt Connected, state stays
            // Disconnected) and "failed" (attempt Failed — server unreachable, a non-auth reason).
            // Pattern: reject, fail, reject, fail, reject, fail — never reaches 3 consecutive
            // rejections because each non-auth failure resets the counter.
            // Contrast with StopsAfterConsecutiveRejections where 3 consecutive Connected-then-
            // closed results trigger the threshold after only 3 attempts. An AuthRejected outcome
            // deliberately does NOT reset the counter — it feeds it (see the AuthRejected tests).
            var sequence = new[]
            {
                ConnectionManager.ConnectionAttemptResult.Connected, ConnectionManager.ConnectionAttemptResult.Failed,
                ConnectionManager.ConnectionAttemptResult.Connected, ConnectionManager.ConnectionAttemptResult.Failed,
                ConnectionManager.ConnectionAttemptResult.Connected, ConnectionManager.ConnectionAttemptResult.Failed,
            };
            await using var cm = new SequenceConnectionManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object,
                attemptResults: sequence
            );

            // Act: the loop terminates ON ITS OWN when the sequence is exhausted (the manager
            // flips _continueToReconnect off); the CTS is a pure hang guard that must NEVER
            // fire. It needs generous headroom: the happy path already spends real time
            // (ConnectionManager's 1s post-create stabilization delay plus one
            // RejectionThreshold window per rejection), and on a loaded CI runner every timer
            // resumption can stretch by seconds — the old 5s budget let runner load truncate
            // the loop mid-sequence, producing the documented "AttemptCount not > 3" CI flake.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            // Assert: more than 3 attempts must have been consumed.
            // If rejection counter weren't being reset by failures, it would stop at 3.
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via sequence exhaustion, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should eventually fail");
            cm.AttemptCount.ShouldBeGreaterThan(3,
                "More than MaxConsecutiveRejections attempts should run — counter was reset by interleaved failures");
        }

        [Fact]
        public async Task Connect_DoesNotTriggerRejection_WhenConnectionStaysAlive()
        {
            // PROTECTIVE INTENT (kept through the C3.4 seam change): a NON-AUTH failed attempt
            // (ConnectionAttemptResult.Failed — server unreachable) must NOT trigger rejection
            // detection. Rejection requires either Connected-then-immediate-close, or an
            // explicit AuthRejected outcome (observable HTTP 401/403) — neither happens here.
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
            // every attempt outcome was Failed (non-auth) — never Connected-then-closed and
            // never AuthRejected.
            result.ShouldBeFalse("Connection should fail (server unreachable)");
            rejectionFired.ShouldBeFalse("OnAuthorizationRejected must not fire when every attempt outcome is Failed (non-auth)");
            cm.AttemptCount.ShouldBeGreaterThan(0, "Should have attempted at least once");
        }

        // ── C3.4 (oauth-client-error-hygiene 02): StatusCode-based rejection counting where
        //    observable — an attempt failing with HTTP 401/403 counts toward the SAME 3-strike
        //    rejection cap as the connected-then-immediately-closed pattern. ──

        [Fact]
        public async Task Connect_AuthRejectedAttempts_TripCap_AndFireAuthorizationRejected()
        {
            // Arrange: every attempt fails with an observable 401/403 (AuthRejected outcome).
            await using var cm = new AuthRejectedConnectionManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object
            );

            var rejectionFired = 0;
            cm.OnAuthorizationRejected.Subscribe(_ => Interlocked.Increment(ref rejectionFired));

            // Act: the loop terminates ON ITS OWN via the rejection cap; the CTS is a pure hang
            // guard that must NEVER fire (generous for loaded CI runners — see the sibling tests).
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            // Assert: same terminal consequence as the connected-then-closed rejection pattern.
            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via the rejection cap, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should fail after repeated 401/403 rejections");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("KeepConnected should be disabled after the rejection cap");
            cm.AttemptCount.ShouldBe(3, "Should have attempted exactly MaxConsecutiveRejections times");
            rejectionFired.ShouldBe(1, "The aggregated 3-strike signal fires exactly once per burst");
        }

        [Fact]
        public async Task Connect_AuthRejectedAttempt_ResetsTheFailureCap_NonAuthFailuresResetRejections()
        {
            // Both reset directions of the C3.4 branch in one discriminating sequence, with the
            // opt-in failure cap at 3:
            //   Failed, Failed, AuthRejected, Failed, Failed, Failed
            // - The AuthRejected at #3 must RESET consecutiveFailures (a 401/403 response proves
            //   the endpoint is reachable), so the failure cap must NOT fire at attempt #4
            //   (which a naive cumulative count would produce) but at attempt #6 — the 3rd
            //   consecutive non-auth failure AFTER the rejection.
            // - The Failed at #4 must RESET consecutiveRejections, so the rejection cap (3) never
            //   fires and OnAuthorizationRejected stays silent.
            var sequence = new[]
            {
                ConnectionManager.ConnectionAttemptResult.Failed, ConnectionManager.ConnectionAttemptResult.Failed,
                ConnectionManager.ConnectionAttemptResult.AuthRejected,
                ConnectionManager.ConnectionAttemptResult.Failed, ConnectionManager.ConnectionAttemptResult.Failed,
                ConnectionManager.ConnectionAttemptResult.Failed,
            };
            await using var cm = new SequenceConnectionManager(
                _logger, _testVersion, _testEndpoint, _mockProvider.Object,
                attemptResults: sequence, maxConsecutiveConnectionFailures: 3
            );

            var rejectionFired = false;
            cm.OnAuthorizationRejected.Subscribe(_ => rejectionFired = true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);

            cts.Token.IsCancellationRequested.ShouldBeFalse(
                "The loop must stop via the failure cap, not the hang-guard CTS");
            result.ShouldBeFalse("Connection should fail");
            cm.AttemptCount.ShouldBe(6,
                "The 401/403 at attempt #3 must reset the consecutive-failure count — the failure cap (3) " +
                "fires only after the 3 consecutive non-auth failures at #4-#6, never at #4 cumulatively");
            rejectionFired.ShouldBeFalse(
                "One 401/403 among non-auth failures must never trip the 3-strike rejection cap — " +
                "the interleaved Failed outcomes reset the rejection counter");
            cm.KeepConnected.CurrentValue.ShouldBeFalse("The failure cap disables KeepConnected");
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
        /// Simulates server rejection: AttemptConnection reports Connected (handshake succeeds)
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

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                // Report Connected (handshake "succeeded") but don't set _connectionState to Connected.
                // StartConnectionLoop will see Disconnected after the stability delay → rejection.
                return Task.FromResult(ConnectionAttemptResult.Connected);
            }
        }

        /// <summary>
        /// Simulates an attempt-level auth rejection: every attempt fails with an observable
        /// HTTP 401/403 (the C3.4 AuthRejected outcome of the failure-reason seam).
        /// </summary>
        private class AuthRejectedConnectionManager : FastConnectionManager
        {
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            public AuthRejectedConnectionManager(
                ILogger logger, Common.Version version, string endpoint, IHubConnectionProvider provider)
                : base(logger, version, endpoint, provider)
            {
            }

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                return Task.FromResult(ConnectionAttemptResult.AuthRejected);
            }
        }

        /// <summary>
        /// Plays a fixed sequence of attempt results. Stops when exhausted.
        /// </summary>
        private class SequenceConnectionManager : FastConnectionManager
        {
            private readonly ConnectionAttemptResult[] _results;
            private int _index;

            public int AttemptCount => _index;

            public SequenceConnectionManager(
                ILogger logger, Common.Version version, string endpoint,
                IHubConnectionProvider provider, ConnectionAttemptResult[] attemptResults,
                int maxConsecutiveConnectionFailures = 0)
                : base(logger, version, endpoint, provider, maxConsecutiveConnectionFailures)
            {
                _results = attemptResults;
            }

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                var idx = Interlocked.Increment(ref _index) - 1;
                if (idx >= _results.Length)
                {
                    _continueToReconnect.Value = false;
                    return Task.FromResult(ConnectionAttemptResult.Failed);
                }
                return Task.FromResult(_results[idx]);
            }
        }

        /// <summary>
        /// Simulates a server that can't be reached: AttemptConnection always fails with a
        /// non-auth outcome. The loop is stopped externally via the CancellationToken passed
        /// to Connect() (or the opt-in failure cap).
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

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                return Task.FromResult(ConnectionAttemptResult.Failed);
            }
        }

        #endregion
    }
}
