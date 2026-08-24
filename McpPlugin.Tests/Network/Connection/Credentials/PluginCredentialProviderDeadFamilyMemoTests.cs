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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// The oauth-client-error-hygiene a2 behaviours (02 §C1/§C2/§C3.3, standing 04 §3.5 "never
    /// loop" ruling): the dead-family memo that gates the NETWORK call after the in-lock store
    /// re-read, its re-arm from the machine store (never from <c>_current</c>), the
    /// <c>invalid_target</c> terminal path with its distinct "server configuration error" reason,
    /// and the low-frequency peer wake-up store re-check that recovers a parked provider when a
    /// peer surface signs in. Every "nothing happened" assertion here has a same-fixture positive
    /// control (binding testing strategy, 02).
    /// </summary>
    public sealed class PluginCredentialProviderDeadFamilyMemoTests : IDisposable
    {
        const string StoredClientId = "app-dcr-4f2a9c31";
        const string ServerTarget = "https://ai-game.dev";
        const string SeededPluginAccess = "eyJ.PLUGIN.sig";
        const string SeededPluginRefresh = "RT-plugin-aaa";

        static readonly DateTimeOffset Start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        static readonly TimeSpan ShortRecheckPeriod = TimeSpan.FromMilliseconds(100);

        readonly string _baseDir;
        DateTimeOffset _now = Start; // the fake clock (a1 seam style): tests move it explicitly

        public PluginCredentialProviderDeadFamilyMemoTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-credmemo-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        void SeedV2(DateTimeOffset? pluginExpiresAt = null)
        {
            NewStore().Write(new MachineCredentials
            {
                ServerTarget = ServerTarget,
                Subject = "usr_123",
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily
                    {
                        AccessToken = SeededPluginAccess,
                        RefreshToken = SeededPluginRefresh,
                        ExpiresAt = pluginExpiresAt ?? Start.AddMinutes(30),
                        ClientId = StoredClientId,
                        Scope = "mcp:plugin",
                    },
                },
            });
        }

        static TokenRefreshResult InvalidGrant() =>
            TokenRefreshResult.Failure("invalid_grant: revoked", TokenRefreshFailureKind.InvalidGrant);

        static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                    return;
                await Task.Delay(20);
            }
            condition().ShouldBeTrue($"condition not reached within {timeout.TotalSeconds:0.#}s");
        }

        // ── Dead ⇒ silent (02 §C1): after the terminal verdict, the memo — not the refresher's
        //    60 s attempt window — blocks every further network attempt this process makes. ──

        [Fact]
        public async Task DeadVerdict_MemoSuppressesEveryFurtherNetworkAttempt_PastTheAttemptWindow()
        {
            SeedV2(pluginExpiresAt: Start.AddMinutes(30));
            var refresher = new FakeTokenRefresher(InvalidGrant());
            var logger = new CapturingLogger();
            using var provider = new PluginCredentialProvider(NewStore(), refresher, logger, clock: () => _now);

            var signInRequiredCount = 0;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredCount++; });

            // The verdict: ONE network attempt, dead family confirmed by the post-failure re-read.
            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            refresher.Requests.Count.ShouldBe(1);
            signInRequiredCount.ShouldBe(1);
            // invalid_grant reason is the raw one — no "server configuration error" class (02 §C2).
            provider.SignInRequiredReason.ShouldBe("invalid_grant: revoked");

            // Advance the fake clock PAST expiry and PAST the production 60 s attempt window
            // (a1 seam): from here every GetAccessTokenAsync enters the proactive-refresh path,
            // and the counting fake has NO rate window — only the memo can block the network.
            _now = Start.AddHours(2);
            for (var i = 0; i < 5; i++)
                (await provider.GetAccessTokenAsync()).ShouldBe(SeededPluginAccess); // stale token, existing contract
            (await provider.RefreshAsync()).ShouldBeFalse();
            (await provider.RefreshAsync()).ShouldBeFalse();

            // ZERO further network attempts — the memo, not the window, blocks (04 §3.5).
            refresher.Requests.Count.ShouldBe(1);
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);

            // ONE structured telemetry event per family death, and no repeated sign-in prompts.
            logger.Warnings.Count(w => w.Contains("Credential family dead")).ShouldBe(1);
            signInRequiredCount.ShouldBe(1);
        }

        // ── Re-arm ⇒ recovery (02 §C1): the comparison is against the freshly-READ store token,
        //    so a peer's differing token clears the memo and refresh proceeds. ──

        [Fact]
        public async Task Rearm_StoreHoldsDifferentRefreshToken_OneNetworkRefresh_ReachesSignedIn()
        {
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(NewStore(), refresher, clock: () => _now);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict
            refresher.Requests.Count.ShouldBe(1);
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);

            // A peer rotates ONLY the refresh token (same access token) — this isolates the memo
            // re-arm from the pre-existing peer-adoption path, which keys on the access token.
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.RefreshToken = "RT-plugin-peer";
            NewStore().Write(peer);

            next = TokenRefreshResult.Success("eyJ.FRESH.sig", "RT-plugin-fresh2", _now.AddHours(1));

            (await provider.RefreshAsync()).ShouldBeTrue();

            refresher.Requests.Count.ShouldBe(2); // the re-armed attempt DID go out
            refresher.Requests[1].RefreshToken.ShouldBe("RT-plugin-peer"); // presenting the STORE's token, not the memoized dead one
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            provider.SignInRequiredReason.ShouldBeNull();
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.FRESH.sig");
        }

        [Fact]
        public async Task Rearm_Control_StoreUnchanged_SameFixtureProducesZeroFurtherAttempts()
        {
            // Vacuity guard for the re-arm test: IDENTICAL fixture, the refresher would even
            // SUCCEED if presented — the only difference is that no peer rewrote the store.
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(NewStore(), refresher, clock: () => _now);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict
            refresher.Requests.Count.ShouldBe(1);

            next = TokenRefreshResult.Success("eyJ.WOULD-WORK.sig", "RT-would-work", _now.AddHours(1));

            (await provider.RefreshAsync()).ShouldBeFalse(); // still suppressed — store unchanged

            refresher.Requests.Count.ShouldBe(1);
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
        }

        [Fact]
        public async Task PeerFullLogin_AdoptedWithoutNetworkCall_SignedIn()
        {
            // The pre-existing peer-adoption path (03 F3.2) is unchanged by the memo: a full fresh
            // login (new access + refresh token) is adopted from the in-lock re-read, no network.
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(NewStore(), refresher, clock: () => _now);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict
            refresher.Requests.Count.ShouldBe(1);

            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.AccessToken = "eyJ.PEER-FRESH.sig";
            peer.Families.Plugin.RefreshToken = "RT-plugin-peer-login";
            peer.Families.Plugin.ExpiresAt = _now.AddHours(1);
            NewStore().Write(peer);

            next = TokenRefreshResult.Failure("must not be called");

            (await provider.RefreshAsync()).ShouldBeTrue();

            refresher.Requests.Count.ShouldBe(1); // adopted, not refreshed
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.PEER-FRESH.sig");
        }

        // ── invalid_target (a1 kind, 02 §C2): same terminal/memo behavior, distinct
        //    "server configuration error" reason in state + telemetry. ──

        [Fact]
        public async Task InvalidTarget_IsTerminal_DistinctServerConfigurationErrorReason_AndMemoBlocks()
        {
            SeedV2(pluginExpiresAt: Start.AddMinutes(30));
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Failure("invalid_target: audience mismatch", TokenRefreshFailureKind.InvalidTarget));
            var logger = new CapturingLogger();
            using var provider = new PluginCredentialProvider(NewStore(), refresher, logger, clock: () => _now);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();

            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            signInRequiredFired.ShouldBeTrue();
            // Distinct reason class (02 §C2) with the raw OAuth error preserved verbatim after it.
            provider.SignInRequiredReason.ShouldBe("server configuration error: invalid_target: audience mismatch");

            // Same memo behavior as invalid_grant: zero further attempts past the window.
            _now = Start.AddHours(2);
            for (var i = 0; i < 3; i++)
                await provider.GetAccessTokenAsync();
            (await provider.RefreshAsync()).ShouldBeFalse();
            refresher.Requests.Count.ShouldBe(1);

            // Telemetry keeps the distinct error code (one event, per family death).
            var dead = logger.Warnings.Where(w => w.Contains("Credential family dead")).ToList();
            dead.Count.ShouldBe(1);
            dead[0].ShouldContain("invalid_target");

            // The store was never touched (inherited ruling): the dead material is still on disk.
            NewStore().Read()!.Families!.Plugin!.RefreshToken.ShouldBe(SeededPluginRefresh);
        }

        [Fact]
        public async Task InvalidTarget_Rearm_OnPeerLogin_RecoversLikeInvalidGrant()
        {
            SeedV2();
            var next = TokenRefreshResult.Failure("invalid_target: audience mismatch", TokenRefreshFailureKind.InvalidTarget);
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(NewStore(), refresher, clock: () => _now);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired); // parked — invalid_target IS terminal

            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.AccessToken = "eyJ.PEER-FRESH.sig";
            peer.Families.Plugin.RefreshToken = "RT-plugin-peer-login";
            NewStore().Write(peer);
            next = TokenRefreshResult.Failure("must not be called");

            (await provider.RefreshAsync()).ShouldBeTrue(); // adopted — same re-arm semantics

            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            provider.SignInRequiredReason.ShouldBeNull();
        }

        // ── Peer wake-up (02 §C3.3): a parked provider recovers within one re-check period,
        //    with NO caller driving the token path. ──

        [Fact]
        public async Task PeerWakeUp_FullPeerLogin_AdoptedWithinRecheckPeriod_WithoutAnyCaller()
        {
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, clock: () => _now, deadFamilyRecheckPeriod: ShortRecheckPeriod);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict — provider parks
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            refresher.Requests.Count.ShouldBe(1);

            // A peer surface signs in (full fresh credential). NOTHING calls the provider again.
            next = TokenRefreshResult.Failure("must not be needed"); // adoption needs no network
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.AccessToken = "eyJ.PEER-LOGIN.sig";
            peer.Families.Plugin.RefreshToken = "RT-plugin-peer-login";
            peer.Families.Plugin.ExpiresAt = _now.AddHours(1);
            NewStore().Write(peer);

            await WaitUntilAsync(() => provider.State.CurrentValue == AuthState.SignedIn, TimeSpan.FromSeconds(10));

            refresher.Requests.Count.ShouldBe(1); // adopted from the store — still zero extra network
            provider.SignInRequiredReason.ShouldBeNull();
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.PEER-LOGIN.sig");
        }

        [Fact]
        public async Task PeerWakeUp_RotatedRefreshTokenOnly_RearmsAndRefreshesWithinRecheckPeriod()
        {
            // The network flavour of the wake-up: the store holds a DIFFERENT refresh token but the
            // same access token, so recovery must go re-arm → one network refresh → SignedIn.
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, clock: () => _now, deadFamilyRecheckPeriod: ShortRecheckPeriod);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict — provider parks
            refresher.Requests.Count.ShouldBe(1);

            next = TokenRefreshResult.Success("eyJ.WOKEN.sig", "RT-plugin-fresh3", _now.AddHours(1));
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.RefreshToken = "RT-plugin-peer";
            NewStore().Write(peer);

            await WaitUntilAsync(() => provider.State.CurrentValue == AuthState.SignedIn, TimeSpan.FromSeconds(10));

            refresher.Requests.Count.ShouldBe(2);
            refresher.Requests[1].RefreshToken.ShouldBe("RT-plugin-peer");
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.WOKEN.sig");
        }

        [Fact]
        public async Task PeerWakeUp_Control_StoreUnchanged_StaysParked_ZeroNetworkAttempts()
        {
            // Control for both wake-up tests: identical fixture (refresher would succeed if
            // presented), the ONLY difference is that the store is never rewritten.
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, clock: () => _now, deadFamilyRecheckPeriod: ShortRecheckPeriod);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict — provider parks
            next = TokenRefreshResult.Success("eyJ.WOULD-WORK.sig", "RT-would-work", _now.AddHours(1));

            await Task.Delay(TimeSpan.FromMilliseconds(700)); // ≥6 re-check periods

            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            refresher.Requests.Count.ShouldBe(1); // the re-check is a lock-free READ — no network
        }

        [Fact]
        public async Task PeerWakeUp_DisposedWhileParked_LoopStops()
        {
            // Mirror of the rotated-refresh-token wake-up: after Dispose the SAME store rewrite
            // must produce NO further network attempt (02 §C3.3 — stops on disposal).
            SeedV2();
            var next = InvalidGrant();
            var refresher = new FakeTokenRefresher(_ => next);
            var provider = new PluginCredentialProvider(
                NewStore(), refresher, clock: () => _now, deadFamilyRecheckPeriod: ShortRecheckPeriod);

            (await provider.RefreshAsync()).ShouldBeFalse(); // the verdict — provider parks
            refresher.Requests.Count.ShouldBe(1);

            provider.Dispose();

            next = TokenRefreshResult.Success("eyJ.WOULD-WORK.sig", "RT-would-work", _now.AddHours(1));
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.RefreshToken = "RT-plugin-peer";
            NewStore().Write(peer);

            await Task.Delay(TimeSpan.FromMilliseconds(500)); // ≥4 would-be re-check periods

            refresher.Requests.Count.ShouldBe(1); // the loop is dead — nothing re-armed or refreshed
        }

        sealed class CapturingLogger : ILogger
        {
            readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel, string)>();

            public IEnumerable<string> Warnings
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
}
