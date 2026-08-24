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
    /// End-to-end (harness level) coverage for oauth-client-error-hygiene 02 §C3 stop/resume:
    /// a REAL <see cref="ConnectionManager"/> retry loop + a REAL
    /// <see cref="PluginCredentialProvider"/> (temp machine store, scripted refresher, fast peer
    /// wake-up re-check) wired by the REAL <see cref="ConnectionCredentialCoordinator"/>. Proves
    /// the loop settles idle-Disconnected after the dead-family verdict (no attempts after it),
    /// resumes when a peer surface's login lands in the machine store, and — the control — that
    /// transient failures with default config still retry unbounded with the provider untouched.
    /// Uses NullLogger: the loops here outlive individual assertions, and a test-output-bound
    /// logger would race test completion.
    /// </summary>
    public sealed class CoordinatorStopResumeHarnessTests : IDisposable
    {
        const string SeededAccess = "eyJ.SEEDED.aaa";
        const string SeededRefresh = "RT-SEEDED-bbb";
        static readonly TimeSpan ShortRecheckPeriod = TimeSpan.FromMilliseconds(100);

        readonly string _baseDir;
        readonly Common.Version _testVersion = new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" };

        public CoordinatorStopResumeHarnessTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-stopresume-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
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

        void SeedStore()
        {
            // v2 plugin-family shape (a v1-style top-level seed would be adopted as families.legacy,
            // and the peer-login rewrite below mutates families.plugin explicitly).
            NewStore().Write(new MachineCredentials
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
        }

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

        // ── DoD: dead family + running loop ⇒ settles idle-Disconnected; peer login ⇒ resumes. ──

        [Fact]
        public async Task DeadFamilyVerdict_StopsRunningLoop_PeerLoginResumes()
        {
            SeedStore();
            var next = TokenRefreshResult.Failure("invalid_grant: revoked", TokenRefreshFailureKind.InvalidGrant);
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, deadFamilyRecheckPeriod: ShortRecheckPeriod);
            await using var cm = new FailingLoopConnectionManager(_testVersion, DummyHubProvider().Object);
            using var coordinator = new ConnectionCredentialCoordinator(cm, provider);

            // A live retry loop (default config: unbounded 5 s retry, accelerated here).
            var connectTask = cm.Connect();
            await WaitUntilAsync(() => cm.AttemptCount >= 2, TimeSpan.FromSeconds(10), "the retry loop must be live");
            cm.KeepConnected.CurrentValue.ShouldBeTrue();

            // The dead-family verdict (a2) — in production the proactive refresh path hits this;
            // the provider surfaces SignInRequired and the coordinator must STOP the loop.
            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);

            // The loop settles idle-Disconnected: Connect() returns, reconnect intent is cleared…
            (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBe(connectTask,
                "the running Connect() must return once the coordinator stops the loop");
            (await connectTask).ShouldBeFalse();
            await WaitUntilAsync(() => !cm.KeepConnected.CurrentValue, TimeSpan.FromSeconds(5), "KeepConnected must clear");

            // …and NO attempts run after the verdict (the hammering this task exists to kill).
            var settledAttempts = cm.AttemptCount;
            await Task.Delay(500);
            cm.AttemptCount.ShouldBe(settledAttempts, "an idle-Disconnected loop must make zero further attempts");
            refresher.Requests.Count.ShouldBe(1, "the dead-family memo blocks every further network refresh");

            // Fake peer login: another surface writes a fresh credential to the machine store.
            next = TokenRefreshResult.Success("eyJ.PEER.ccc", "RT-PEER-ddd", DateTimeOffset.UtcNow.AddHours(1));
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.RefreshToken = "RT-plugin-peer";
            NewStore().Write(peer);

            // Wake-up re-check re-arms + refreshes ⇒ SignedIn edge ⇒ the coordinator RESUMES the loop.
            await WaitUntilAsync(() => provider.State.CurrentValue == AuthState.SignedIn, TimeSpan.FromSeconds(10),
                "the peer login must wake the parked provider");
            await WaitUntilAsync(() => cm.AttemptCount > settledAttempts, TimeSpan.FromSeconds(10),
                "the SignedIn edge must resume the connection loop");
            cm.KeepConnected.CurrentValue.ShouldBeTrue("the resumed loop re-arms reconnect intent");
        }

        // ── The 3-strike flavour: rejection cap → coordinator refresh → dead verdict → settle;
        //    then a peer login resumes (intent captured through the rejection-handling pass). ──

        [Fact]
        public async Task ThreeStrikeRejection_DeadFamily_SettlesIdle_ThenPeerLoginResumes()
        {
            SeedStore();
            var next = TokenRefreshResult.Failure("invalid_grant: revoked", TokenRefreshFailureKind.InvalidGrant);
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, deadFamilyRecheckPeriod: ShortRecheckPeriod);
            await using var cm = new RejectedLoopConnectionManager(_testVersion, DummyHubProvider().Object);
            using var coordinator = new ConnectionCredentialCoordinator(cm, provider);

            // The loop 3-strikes on server-side rejection, stops itself, and fires the aggregated
            // signal; the coordinator's refresh then returns the dead-family verdict.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await cm.Connect(cts.Token);
            result.ShouldBeFalse();
            cm.KeepConnected.CurrentValue.ShouldBeFalse("the rejection cap stops the loop");

            await WaitUntilAsync(() => provider.State.CurrentValue == AuthState.SignInRequired, TimeSpan.FromSeconds(10),
                "the rejection-driven refresh must surface the dead-family verdict");

            // Settled: no further attempts, exactly one network refresh.
            var settledAttempts = cm.AttemptCount;
            await Task.Delay(500);
            cm.AttemptCount.ShouldBe(settledAttempts, "after the verdict the loop must stay idle-Disconnected");
            refresher.Requests.Count.ShouldBe(1);

            // Peer login. Intent was captured through the rejection-handling pass (the cap cleared
            // KeepConnected BEFORE the verdict), so the SignedIn edge must still resume.
            next = TokenRefreshResult.Success("eyJ.PEER.ccc", "RT-PEER-ddd", DateTimeOffset.UtcNow.AddHours(1));
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.RefreshToken = "RT-plugin-peer";
            NewStore().Write(peer);

            await WaitUntilAsync(() => provider.State.CurrentValue == AuthState.SignedIn, TimeSpan.FromSeconds(10),
                "the peer login must wake the parked provider");
            await WaitUntilAsync(() => cm.AttemptCount > settledAttempts, TimeSpan.FromSeconds(10),
                "the SignedIn edge must resume the connection loop even though the cap had already cleared KeepConnected");
        }

        // ── Control (binding testing strategy, 02): transient failures with DEFAULT config keep
        //    retrying unbounded — no stop, no verdict, provider untouched. ──

        [Fact]
        public async Task TransientFailures_DefaultConfig_RetryUnbounded_NoStopNoVerdict()
        {
            SeedStore();
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success("eyJ.WOULD-WORK.eee", "RT-would-work", DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, deadFamilyRecheckPeriod: ShortRecheckPeriod);
            await using var cm = new FailingLoopConnectionManager(_testVersion, DummyHubProvider().Object);
            using var coordinator = new ConnectionCredentialCoordinator(cm, provider);

            var connectTask = cm.Connect();

            // The loop keeps retrying well past any cap the auth path would impose (3).
            await WaitUntilAsync(() => cm.AttemptCount > 5, TimeSpan.FromSeconds(10),
                "transient failures with default config must retry unbounded");
            cm.KeepConnected.CurrentValue.ShouldBeTrue("nothing may clear the reconnect intent");
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn, "no verdict may surface from transient connection failures");
            refresher.Requests.Count.ShouldBe(0, "no refresh may be driven by transient connection failures");

            await cm.Disconnect();
            (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10)))).ShouldBe(connectTask);
        }

        /// <summary>Real loop, accelerated pacing, every attempt fails for a NON-AUTH reason (unreachable endpoint).</summary>
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

        /// <summary>Real loop, accelerated pacing, server "accepts the handshake then closes" — the 3-strike rejection pattern.</summary>
        private class RejectedLoopConnectionManager : ConnectionManager
        {
            private int _attemptCount;
            public int AttemptCount => _attemptCount;

            protected override TimeSpan RejectionThreshold { get; } = TimeSpan.FromMilliseconds(50);

            public RejectedLoopConnectionManager(Common.Version version, IHubConnectionProvider provider)
                : base(NullLogger.Instance, version, "http://localhost:9999/dummy", provider)
            {
            }

            protected override Task WaitBeforeRetry(CancellationToken cancellationToken)
                => Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);

            protected override Task<ConnectionAttemptResult> AttemptConnection(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _attemptCount);
                // Connected (handshake "succeeds") but the hub state stays Disconnected ⇒ the loop
                // counts it as a server-side rejection after the stability window.
                return Task.FromResult(ConnectionAttemptResult.Connected);
            }
        }
    }
}
