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

        PluginCredentialProvider SeededProvider(ITokenRefresher refresher)
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
            var refresher = RefresherReturning(TokenRefreshResult.Failure("refresh token expired"));
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

        /// <summary>
        /// A minimal <see cref="IConnection"/> test double: exposes a rejection signal to fire, records
        /// reconnect attempts, and signals each <see cref="Connect"/> via a task so tests can await the
        /// fire-and-forget handler deterministically.
        /// </summary>
        sealed class FakeConnection : IConnection
        {
            readonly ReactiveProperty<bool> _keepConnected = new ReactiveProperty<bool>(true);
            readonly ReactiveProperty<HubConnectionState> _state = new ReactiveProperty<HubConnectionState>(HubConnectionState.Disconnected);
            readonly ReadOnlyReactiveProperty<bool> _keepConnectedRo;
            readonly ReadOnlyReactiveProperty<HubConnectionState> _stateRo;
            readonly Subject<Unit> _authRejected = new Subject<Unit>();
            readonly object _sync = new object();
            // Single-shot: completes on the FIRST Connect and stays completed, so a waiter registered
            // before OR after the fire-and-forget handler runs observes it (no TCS-replacement race).
            readonly TaskCompletionSource<bool> _firstConnectSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public FakeConnection()
            {
                _keepConnectedRo = _keepConnected.ToReadOnlyReactiveProperty();
                _stateRo = _state.ToReadOnlyReactiveProperty();
            }

            public int ConnectCount { get; private set; }

            public ReadOnlyReactiveProperty<bool> KeepConnected => _keepConnectedRo;
            public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _stateRo;
            public Observable<Unit> OnAuthorizationRejected => _authRejected;

            public void FireAuthorizationRejected() => _authRejected.OnNext(Unit.Default);

            public async Task<bool> WaitForNextConnectAsync(TimeSpan timeout)
            {
                var completed = await Task.WhenAny(_firstConnectSignal.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return completed == _firstConnectSignal.Task;
            }

            public Task<bool> Connect(CancellationToken cancellationToken = default)
            {
                lock (_sync)
                {
                    ConnectCount++;
                    _state.Value = HubConnectionState.Connected;
                    _firstConnectSignal.TrySetResult(true);
                }
                return Task.FromResult(true);
            }

            public Task Disconnect(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
