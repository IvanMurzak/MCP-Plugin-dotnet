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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// <para>
    /// <b>The defect.</b> <c>IConnection.Connect</c> declares
    /// <c>Task&lt;bool&gt; Connect(CancellationToken cancellationToken = default)</c>. With the
    /// DEFAULT argument the token is <see cref="CancellationToken.None"/>, which
    /// <c>CanBeCanceled == false</c> — nothing can ever cancel it. Combined with the DEFAULT
    /// reconnect cap (<c>MaxConsecutiveConnectionFailures == 0</c> = retry an unreachable endpoint
    /// forever) the awaited <c>Connect()</c> task has <b>no terminating condition at all</b>: the
    /// retry loop cannot give up, the token cannot fire, so the caller's <c>await</c> never
    /// completes.
    /// </para>
    /// <para>
    /// <b>Why that is not merely academic.</b> <see cref="ConnectionCredentialCoordinator"/> holds
    /// a single-flight latch ACROSS that await. <c>HandleRejectionAsync</c> takes
    /// <c>_handleGate</c> (a <c>SemaphoreSlim(1,1)</c>, acquired with <c>WaitAsync(0)</c>) and
    /// releases it in a <c>finally</c> that sits AFTER <c>await _connection.Connect(...)</c>. If
    /// that await never completes the <c>finally</c> never runs, so the semaphore is never
    /// released — and from then on EVERY <c>HandleRejectionAsync</c> call fails its
    /// <c>WaitAsync(0)</c> and returns false without even attempting a refresh. The
    /// refresh-token-on-authorization-rejection path is dead for the rest of the process lifetime.
    /// </para>
    /// <para>
    /// The connection managers below subclass the real <see cref="ConnectionManager"/> and override
    /// ONLY pacing and the attempt outcome — exactly the seams
    /// <c>CoordinatorStopResumeHarnessTests</c> already uses. The retry loop, the gate, the
    /// single-flight slot and the whole <c>Connect</c> control flow are the real production ones;
    /// the overrides just make "the endpoint is unreachable" deterministic and fast instead of
    /// waiting out real 5 s backoffs.
    /// </para>
    /// </summary>
    public sealed class ConnectTokenlessUnboundedAwaitTests : IDisposable
    {
        const string SeededAccess = "eyJ.SEEDED.aaa";
        const string SeededRefresh = "RT-SEEDED-bbb";

        readonly string _baseDir;
        readonly Common.Version _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };

        public ConnectTokenlessUnboundedAwaitTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-tokenless-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
            {
                try { Directory.Delete(parent, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        void SeedStore() => NewStore().Write(new MachineCredentials
        {
            ServerTarget = "https://ai-game.dev",
            Families = new MachineCredentialFamilies
            {
                Plugin = new MachineCredentialFamily
                {
                    AccessToken = SeededAccess,
                    RefreshToken = SeededRefresh,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    ClientId = "app-dcr-4f2a9c31",
                    Scope = "mcp:plugin",
                },
            },
        });

        static Mock<IHubConnectionProvider> DummyHubProvider()
        {
            var provider = new Mock<IHubConnectionProvider>();
            provider
                .Setup(x => x.CreateConnectionAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new HubConnectionBuilder()
                    .WithUrl("http://localhost:9999/dummy", options =>
                    {
                        options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                        options.SkipNegotiation = true;
                    })
                    .Build());
            return provider;
        }

        static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string because)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                    return;
                await Task.Delay(20);
            }
            condition().ShouldBeTrue($"{because} (not reached within {timeout.TotalSeconds:0.#}s)");
        }

        /// <summary>
        /// Bounded await. EVERY await of a Connect-derived task in this file goes through this,
        /// because the defect under test is precisely "an await that never completes": awaiting such
        /// a task directly would make a regression HANG the test host instead of failing it, which
        /// wedges CI for the whole job timeout and is indistinguishable from a stuck runner. Found
        /// the hard way — an earlier draft of these guards hung for 35 minutes under one plant.
        /// </summary>
        static async Task<T> BoundedAsync<T>(Task<T> task, string because, int seconds = 10)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
            completed.ShouldBe(task, $"{because} (did not complete within {seconds}s)");
            return await task;
        }

        static async Task BoundedAsync(Task task, string because, int seconds = 10)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
            completed.ShouldBe(task, $"{because} (did not complete within {seconds}s)");
            await task;
        }

        // ───────────────────────────────────────────────────────────────────────────────────────
        // GUARD 1 — the defect itself: a token-less Connect() against an unreachable endpoint must
        // hand the caller back control instead of awaiting a retry loop that can never terminate.
        // ───────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConnectWithoutToken_AgainstUnreachableEndpoint_ReturnsToTheCaller()
        {
            await using var cm = new FailingLoopConnectionManager(_testVersion, DummyHubProvider().Object);

            // The exact production call shape: IConnection.Connect()'s DEFAULT argument, i.e.
            // CancellationToken.None. Nothing can cancel it, and the default reconnect cap is
            // unlimited — so before the fix this task had no terminating condition whatsoever.
            var connectTask = cm.Connect();

            (await BoundedAsync(connectTask,
                "Connect() with a NON-CANCELLABLE token must not hold the caller hostage to an " +
                "unbounded retry loop — there is no token that could ever release it"))
                .ShouldBeFalse("the endpoint is unreachable, so the attempt failed");

            // …and the unlimited-retry contract is preserved: giving the caller back control must
            // NOT stop reconnecting. The loop keeps running in the background past any cap.
            await WaitUntilAsync(() => cm.AttemptCount > 5, TimeSpan.FromSeconds(10),
                "the background retry loop must keep retrying an unreachable endpoint");
            cm.KeepConnected.CurrentValue.ShouldBeTrue(
                "returning to the caller must not clear the reconnect intent — only an explicit " +
                "Disconnect or a cap may do that");

            await cm.Disconnect();
        }

        // ───────────────────────────────────────────────────────────────────────────────────────
        // GUARD 2 — the user-visible consequence: the coordinator's single-flight latch is held
        // across that await, so a never-completing Connect() permanently kills the
        // refresh-on-authorization-rejection path.
        // ───────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task HangingConnect_MustNotWedge_TheRejectionHandlingGate()
        {
            SeedStore();
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success("eyJ.FRESH.ccc", "RT-fresh-ddd", DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(NewStore(), refresher);
            await using var cm = new FailingLoopConnectionManager(_testVersion, DummyHubProvider().Object);
            using var coordinator = new ConnectionCredentialCoordinator(cm, provider);

            // Pass 1: the real production flow after a 3-strike rejection — refresh the credential,
            // then reconnect. The endpoint is unreachable, so Connect() enters the unbounded loop.
            var firstPass = coordinator.HandleRejectionAsync();

            await WaitUntilAsync(() => cm.AttemptCount >= 2, TimeSpan.FromSeconds(10),
                "pass 1 must have reached the reconnect loop (i.e. we are inside its Connect await)");
            refresher.Requests.Count.ShouldBe(1, "pass 1 refreshed the credential");

            // Pass 2: a later rejection burst. It MUST be able to run — before the fix the
            // SemaphoreSlim(1,1) taken by pass 1 was never released, so WaitAsync(0) failed and
            // this returned false without even attempting a refresh, forever.
            var secondPass = await BoundedAsync(coordinator.HandleRejectionAsync(),
                "a later rejection pass must not be able to block forever either");
            secondPass.ShouldBeTrue(
                "a later authorization rejection must still be handled — the single-flight gate " +
                "must not stay wedged behind pass 1's Connect await");
            refresher.Requests.Count.ShouldBe(2,
                "pass 2 must actually reach the network refresh; a wedged gate short-circuits " +
                "before it, which is exactly how the recovery path dies silently");

            await cm.Disconnect();
            await BoundedAsync(firstPass, "pass 1 must settle once the loop is stopped");
        }

        // ───────────────────────────────────────────────────────────────────────────────────────
        // GUARD 3 — the single-flight JOINER has the same exposure as the leader: it awaits the
        // leader's attempt proxy, which completes only when the leader's (possibly unbounded) loop
        // ends. A joiner that also cannot cancel would therefore inherit exactly the same
        // never-completing await, so it must attach to the first decided outcome instead.
        // ───────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConcurrentTokenlessConnects_BothReturn_AndShareOneAttempt()
        {
            var hubProvider = DummyHubProvider();
            await using var cm = new FailingLoopConnectionManager(_testVersion, hubProvider.Object);

            var leader = cm.Connect();

            // Let the leader install itself as the single-flight owner and get the loop running,
            // so the second call genuinely JOINS rather than becoming a second leader.
            await WaitUntilAsync(() => cm.AttemptCount >= 1, TimeSpan.FromSeconds(10),
                "the leader must own the in-flight attempt before the joiner arrives");

            var joiner = cm.Connect();

            await BoundedAsync(Task.WhenAll(leader, joiner),
                "a token-less JOINER must not inherit the leader's unbounded await either");

            (await leader).ShouldBeFalse();
            (await joiner).ShouldBeFalse();

            // Single-flight still holds: joining must not have started a second connection.
            hubProvider.Verify(x => x.CreateConnectionAsync(It.IsAny<string>()), Times.Once);

            await cm.Disconnect();
        }

        // ───────────────────────────────────────────────────────────────────────────────────────
        // CONTROL — the godot#78513 contract is untouched. An EXPLICIT cancellation token still
        // gets the historical behaviour: retry forever, return only when that token cancels.
        // This is the half that must NOT change, and it is asserted here so that any future
        // attempt to "simplify" guard 1 into applying to every caller reddens immediately.
        // ───────────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public async Task ConnectWithExplicitToken_StillRunsUntilThatTokenCancels()
        {
            await using var cm = new FailingLoopConnectionManager(_testVersion, DummyHubProvider().Object);

            // The token is cancelled by the TEST, only after the loop has demonstrably retried —
            // never by a timer. An earlier form used a 2 s CancelAfter and then asserted
            // "AttemptCount > 5": that counted how many 25 ms retries fit in 2 s of wall clock, a
            // fixed budget against a load-dependent cost, and it went red on a saturated hosted
            // runner (4 attempts) with the contract fully intact. Ordering events instead of
            // timing them asserts the same two properties with no clock in the verdict.
            using var cts = new CancellationTokenSource();
            var connectTask = cm.Connect(cts.Token);

            // (1) It keeps retrying: several failed attempts happen while the caller still waits.
            // The timeout is only a hang guard for a regressed loop, not a pacing assumption.
            await WaitUntilAsync(() => cm.AttemptCount > 5 || connectTask.IsCompleted, TimeSpan.FromSeconds(60),
                "an explicit-token Connect must keep retrying the unreachable endpoint");
            connectTask.IsCompleted.ShouldBeFalse(
                "the awaited Connect(token) must still be running while its token is live — an " +
                "EXPLICIT token keeps the unlimited-retry contract; returning on the first failed " +
                "attempt is the token-less behaviour leaking onto every caller");
            cm.AttemptCount.ShouldBeGreaterThan(5,
                "and it must have kept retrying the unreachable endpoint the whole time");

            // (2) Only the token stops it: cancelling releases the caller with a failed outcome.
            cts.Cancel();
            var result = await BoundedAsync(connectTask,
                "cancelling the explicit token must release the awaited Connect", seconds: 30);
            result.ShouldBeFalse();
        }

        /// <summary>
        /// Real <see cref="ConnectionManager"/>; only pacing and the attempt outcome are overridden,
        /// so "the endpoint is unreachable" is deterministic and fast. Mirrors the equivalent double
        /// in <c>CoordinatorStopResumeHarnessTests</c>.
        /// </summary>
        private class FailingLoopConnectionManager : ConnectionManager
        {
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            protected override TimeSpan RejectionThreshold { get; } = TimeSpan.FromMilliseconds(50);

            public FailingLoopConnectionManager(Common.Version version, IHubConnectionProvider provider)
                : base(NullLogger.Instance, version, "http://localhost:9999/dummy", provider)
            {
            }

            protected override Task WaitBeforeRetry(CancellationToken cancellationToken)
                => Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                return Task.FromResult(ConnectionAttemptResult.Failed);
            }
        }
    }
}
