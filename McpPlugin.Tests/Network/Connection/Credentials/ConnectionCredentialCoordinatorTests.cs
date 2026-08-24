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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// Coverage for the mcp-authorize b7 refresh-on-reject loop: the connection layer's 3-strike
    /// authorization-rejection signal drives a token refresh and, on success, a reconnect; on refresh
    /// failure the coordinator stops and the provider surfaces sign-in-again.
    /// </summary>
    public sealed class ConnectionCredentialCoordinatorTests : IDisposable
    {
        const string SeededAccessToken = "eyJ.SEEDED.aaa";
        const string SeededRefreshToken = "RT-SEEDED-bbb";
        const string RefreshedAccessToken = "eyJ.REFRESHED.ccc";

        readonly string _baseDir;

        public ConnectionCredentialCoordinatorTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-coord-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        PluginCredentialProvider SeededProvider(ITokenRefresher? refresher)
        {
            NewStore().Write(new MachineCredentials
            {
                AccessToken = SeededAccessToken,
                RefreshToken = SeededRefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ServerTarget = "https://ai-game.dev",
            });
            return new PluginCredentialProvider(NewStore(), refresher);
        }

        static Mock<ITokenRefresher> RefresherReturning(TokenRefreshResult result)
        {
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            // The provider invokes the family-aware overload (unified-machine-auth b3); the proxy
            // intercepts the interface's default implementation, so it needs its own setup.
            refresher
                .Setup(r => r.RefreshAsync(It.IsAny<TokenRefreshRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            return refresher;
        }

        // ── DoD: 3-strike signal → refresh → reconnect. ──

        [Fact]
        public async Task HandleRejection_RefreshSucceeds_Reconnects()
        {
            var refresher = RefresherReturning(TokenRefreshResult.Success(RefreshedAccessToken, expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection();
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            var handled = await coordinator.HandleRejectionAsync();

            handled.ShouldBeTrue();
            connection.ConnectCount.ShouldBe(1);
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            (await provider.GetAccessTokenAsync()).ShouldBe(RefreshedAccessToken);
        }

        [Fact]
        public async Task HandleRejection_RefreshFails_DoesNotReconnect_SurfacesSignInRequired()
        {
            // "refresh token expired" IS an invalid_grant — the dead-family kind, the ONLY kind
            // that reaches SignInRequired (review B2; a transient failure stays signed in).
            var refresher = RefresherReturning(TokenRefreshResult.Failure("refresh token expired", TokenRefreshFailureKind.InvalidGrant));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection();
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            var handled = await coordinator.HandleRejectionAsync();

            handled.ShouldBeFalse();
            connection.ConnectCount.ShouldBe(0);
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
        }

        // ── The subscription is actually wired: firing OnAuthorizationRejected drives the refresh+reconnect. ──

        [Fact]
        public async Task OnAuthorizationRejected_Signal_TriggersRefreshAndReconnect()
        {
            var refresher = RefresherReturning(TokenRefreshResult.Success(RefreshedAccessToken, expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection();
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            // Simulate the ConnectionManager firing its aggregated 3-strike rejection signal.
            connection.FireAuthorizationRejected();

            // The handler runs fire-and-forget; await the Connect it drives (bounded).
            var reconnected = await connection.WaitForNextConnectAsync(TimeSpan.FromSeconds(5));
            reconnected.ShouldBeTrue("the rejection signal should drive a reconnect");
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
        }

        // ── C3.1 stop (oauth-client-error-hygiene 02): the dead-family verdict must stop the
        //    connect loop via the existing stop primitive, exactly once, single-flight. ──

        [Fact]
        public async Task OnSignInRequired_StopsReconnect_ExactlyOneDisconnect()
        {
            var refresher = RefresherReturning(TokenRefreshResult.Failure("refresh token expired", TokenRefreshFailureKind.InvalidGrant));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection(keepConnected: true);
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            // The dead-family verdict (surfaced by the provider) — the coordinator must stop the loop.
            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);

            (await connection.WaitForNextDisconnectAsync(TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the sign-in-required verdict must drive Disconnect (the stop primitive)");
            connection.DisconnectCount.ShouldBe(1);
            connection.ConnectCount.ShouldBe(0, "stopping must not re-enter any recovery path (P1-26)");
        }

        [Fact]
        public async Task OnSignInRequired_FlappingVerdicts_SingleFlight_StillOneDisconnect()
        {
            // A provider WITHOUT a refresher surfaces sign-in-required on EVERY RefreshAsync call —
            // a real (not simulated) flapping OnSignInRequired source. Holding the Disconnect task
            // pending across all three verdicts proves the stop pass is single-flight: the second
            // and third signals arrive while the first pass is still in flight and must not stack.
            using var provider = SeededProvider(refresher: null);
            using var connection = new FakeConnection(keepConnected: true);
            connection.HoldDisconnects();
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            (await provider.RefreshAsync()).ShouldBeFalse();
            (await provider.RefreshAsync()).ShouldBeFalse();
            (await provider.RefreshAsync()).ShouldBeFalse();

            (await connection.WaitForNextDisconnectAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            connection.DisconnectCount.ShouldBe(1, "three verdicts while the stop pass is in flight ⇒ still exactly one Disconnect");

            connection.ReleaseDisconnects();
            await Task.Delay(100); // let the pass complete — nothing further may run
            connection.DisconnectCount.ShouldBe(1);
        }

        // ── C3.2 resume (edge-triggered, intent-gated, never while a loop is live). ──

        [Fact]
        public async Task SignedInEdge_ResumesConnect_ExactlyOnce_WhenIntentWasOn()
        {
            var refresher = RefresherReturning(TokenRefreshResult.Failure("revoked", TokenRefreshFailureKind.InvalidGrant));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection(keepConnected: true); // the consumer's loop is live
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            // Stop: verdict → Disconnect (clears KeepConnected, as the real manager does).
            (await provider.RefreshAsync()).ShouldBeFalse();
            (await connection.WaitForNextDisconnectAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            connection.KeepConnected.CurrentValue.ShouldBeFalse();

            // Resume: authorization returns (a fresh sign-in adoption) ⇒ SignInRequired→SignedIn edge.
            provider.Adopt(FreshCredentials());
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);

            (await connection.WaitForNextConnectAsync(TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the SignedIn edge must resume the connection (intent was on)");
            connection.ConnectCount.ShouldBe(1, "edge-triggered, single-flight ⇒ exactly one Connect");
        }

        [Fact]
        public async Task SignedInEdge_DoesNotConnect_WhenIntentWasOff()
        {
            // Identical fixture to SignedInEdge_ResumesConnect... (the same-fixture positive
            // control) EXCEPT the consumer never had the loop on: resume must not resurrect a
            // connection the consumer never wanted.
            var refresher = RefresherReturning(TokenRefreshResult.Failure("revoked", TokenRefreshFailureKind.InvalidGrant));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection(keepConnected: false); // intent OFF
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);

            provider.Adopt(FreshCredentials());
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);

            (await connection.WaitForNextConnectAsync(TimeSpan.FromMilliseconds(300)))
                .ShouldBeFalse("with the consumer's KeepConnected intent off, the SignedIn edge must not connect");
            connection.ConnectCount.ShouldBe(0);
        }

        [Fact]
        public async Task SignedInEdge_DoesNotConnect_WhileALoopIsAlreadyLive()
        {
            // Identical fixture to SignedInEdge_ResumesConnect... (the same-fixture positive
            // control) EXCEPT the consumer manually restarted the loop between the stop and the
            // sign-in: resuming would STACK a second loop (review finding 12).
            var refresher = RefresherReturning(TokenRefreshResult.Failure("revoked", TokenRefreshFailureKind.InvalidGrant));
            using var provider = SeededProvider(refresher.Object);
            using var connection = new FakeConnection(keepConnected: true);
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            (await provider.RefreshAsync()).ShouldBeFalse();
            (await connection.WaitForNextDisconnectAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

            connection.SetKeepConnected(true); // the consumer manually reconnected — a loop is live

            provider.Adopt(FreshCredentials());
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);

            (await connection.WaitForNextConnectAsync(TimeSpan.FromMilliseconds(300)))
                .ShouldBeFalse("while a connect loop is already live, the SignedIn edge must be a no-op");
            connection.ConnectCount.ShouldBe(0);
        }

        [Fact]
        public async Task SignedOutToSignedIn_IsNotTheResumeEdge()
        {
            // Edge discipline: only the SignInRequired→SignedIn transition resumes. A plain
            // SignedOut→SignedIn sign-in (no stop happened) must not auto-connect.
            using var provider = new PluginCredentialProvider(NewStore(), refresher: null); // empty store ⇒ SignedOut
            provider.State.CurrentValue.ShouldBe(AuthState.SignedOut);
            using var connection = new FakeConnection(keepConnected: true);
            using var coordinator = new ConnectionCredentialCoordinator(connection, provider);

            provider.Adopt(FreshCredentials());
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);

            (await connection.WaitForNextConnectAsync(TimeSpan.FromMilliseconds(300)))
                .ShouldBeFalse("SignedOut→SignedIn is not the resume edge — the engine drives its own first connect");
            connection.ConnectCount.ShouldBe(0);
        }

        static MachineCredentials FreshCredentials() => new MachineCredentials
        {
            AccessToken = "eyJ.FRESH-LOGIN.zzz",
            RefreshToken = "RT-FRESH-yyy",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ServerTarget = "https://ai-game.dev",
        };

        /// <summary>
        /// A minimal <see cref="IConnection"/> test double: exposes a rejection signal to fire, records
        /// connect/disconnect passes, and signals each via a task so tests can await the fire-and-forget
        /// handlers deterministically. Mirrors the real manager's KeepConnected semantics —
        /// <see cref="Connect"/> arms it, <see cref="Disconnect"/> clears it — because the coordinator's
        /// intent capture and loop-live check read it. <see cref="HoldDisconnects"/> keeps the Disconnect
        /// task pending so a flapping fixture can prove the stop pass is single-flight.
        /// </summary>
        sealed class FakeConnection : IConnection
        {
            readonly ReactiveProperty<bool> _keepConnected;
            readonly ReactiveProperty<HubConnectionState> _state = new ReactiveProperty<HubConnectionState>(HubConnectionState.Disconnected);
            readonly ReadOnlyReactiveProperty<bool> _keepConnectedRo;
            readonly ReadOnlyReactiveProperty<HubConnectionState> _stateRo;
            readonly Subject<Unit> _authRejected = new Subject<Unit>();
            readonly object _sync = new object();
            // Single-shot: completes on the FIRST Connect and stays completed, so a waiter registered
            // before OR after the fire-and-forget handler runs observes it (no TCS-replacement race).
            readonly TaskCompletionSource<bool> _firstConnectSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> _firstDisconnectSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool>? _heldDisconnects;

            public FakeConnection(bool keepConnected = true)
            {
                _keepConnected = new ReactiveProperty<bool>(keepConnected);
                _keepConnectedRo = _keepConnected.ToReadOnlyReactiveProperty();
                _stateRo = _state.ToReadOnlyReactiveProperty();
            }

            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }

            public ReadOnlyReactiveProperty<bool> KeepConnected => _keepConnectedRo;
            public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _stateRo;
            public Observable<Unit> OnAuthorizationRejected => _authRejected;

            public void FireAuthorizationRejected() => _authRejected.OnNext(Unit.Default);

            /// <summary>Fixture control: model a consumer manually starting/stopping the loop.</summary>
            public void SetKeepConnected(bool value) => _keepConnected.Value = value;

            /// <summary>Every Disconnect call from now on returns a task held pending until <see cref="ReleaseDisconnects"/>.</summary>
            public void HoldDisconnects()
            {
                lock (_sync)
                    _heldDisconnects = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void ReleaseDisconnects()
            {
                TaskCompletionSource<bool>? held;
                lock (_sync)
                {
                    held = _heldDisconnects;
                    _heldDisconnects = null;
                }
                held?.TrySetResult(true);
            }

            public async Task<bool> WaitForNextConnectAsync(TimeSpan timeout)
            {
                var completed = await Task.WhenAny(_firstConnectSignal.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return completed == _firstConnectSignal.Task;
            }

            public async Task<bool> WaitForNextDisconnectAsync(TimeSpan timeout)
            {
                var completed = await Task.WhenAny(_firstDisconnectSignal.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return completed == _firstDisconnectSignal.Task;
            }

            public Task<bool> Connect(CancellationToken cancellationToken = default)
            {
                lock (_sync)
                {
                    ConnectCount++;
                    _keepConnected.Value = true; // the real manager arms reconnect intent in ConnectCore
                    _state.Value = HubConnectionState.Connected;
                    _firstConnectSignal.TrySetResult(true);
                }
                return Task.FromResult(true);
            }

            public Task Disconnect(CancellationToken cancellationToken = default)
            {
                Task pending;
                lock (_sync)
                {
                    DisconnectCount++;
                    _keepConnected.Value = false; // the real stop primitive clears _continueToReconnect
                    _state.Value = HubConnectionState.Disconnected;
                    _firstDisconnectSignal.TrySetResult(true);
                    pending = _heldDisconnects?.Task ?? Task.CompletedTask;
                }
                return pending;
            }

            public void DisconnectImmediate() { }
            public bool WaitForImmediateTeardown(TimeSpan timeout) => true;

            public void Dispose()
            {
                _authRejected.Dispose();
                _keepConnectedRo.Dispose();
                _stateRo.Dispose();
                _keepConnected.Dispose();
                _state.Dispose();
            }
        }
    }
}
