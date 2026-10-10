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
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    public sealed class BaseHubConnectorRecoveryTests
    {
        static VersionHandshakeResponse Compatible() => new VersionHandshakeResponse { Compatible = true };

        [Fact]
        public async Task TransientHandshakeFailuresBeyondOldCap_RecoverOnSameTransport()
        {
            using var fixture = new Fixture();
            var calls = 0;
            fixture.Response = _ => Task.FromResult(Interlocked.Increment(ref calls) <= 4
                ? new VersionHandshakeResponse { IsConnectionError = true, Message = "temporarily unavailable" }
                : Compatible());
            fixture.TransportConnected.OnNext(Unit.Default);

            await fixture.Connector.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            calls.ShouldBe(5);
            fixture.Manager.Verify(m => m.DisconnectImmediate(), Times.Never);
            fixture.Manager.Verify(m => m.SetConnected(), Times.Once);
        }

        [Fact]
        public async Task HungHandshakeAttempt_TimesOutAndRecoversWithoutStoppingTransport()
        {
            using var fixture = new Fixture();
            fixture.Connector.AttemptTimeout = TimeSpan.FromMilliseconds(30);
            var calls = 0;
            fixture.Response = async token =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    await Task.Delay(Timeout.Infinite, token);
                return Compatible();
            };

            fixture.TransportConnected.OnNext(Unit.Default);
            await fixture.Connector.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            calls.ShouldBe(2);
            fixture.Manager.Verify(m => m.DisconnectImmediate(), Times.Never);
            fixture.Manager.Verify(m => m.SetConnected(), Times.Once);
        }

        [Fact]
        public void ExplicitIncompatibleVersion_StopsWithoutRetrying()
        {
            using var fixture = new Fixture();
            fixture.Response = _ => Task.FromResult(new VersionHandshakeResponse { Compatible = false });

            fixture.TransportConnected.OnNext(Unit.Default);

            fixture.Manager.Verify(m => m.DisconnectImmediate(), Times.Once);
            fixture.Manager.Verify(m => m.SetConnected(), Times.Never);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PendingHandshakeRetry_StopsOnDisconnectOrDisposal(bool dispose)
        {
            using var fixture = new Fixture();
            var calls = 0;
            fixture.Response = _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new VersionHandshakeResponse { IsConnectionError = true });
            };
            fixture.Connector.Delay = token => Task.Delay(Timeout.Infinite, token);
            fixture.TransportConnected.OnNext(Unit.Default);
            await fixture.Connector.RetryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (dispose)
                fixture.Connector.Dispose();
            else
                await fixture.Connector.Disconnect();
            await Task.Delay(100);

            calls.ShouldBe(1);
            fixture.Manager.Verify(m => m.SetConnected(), Times.Never);
        }

        [Fact]
        public async Task TransportReplacedDuringHandshake_QueuesNewHandshakeAndIgnoresOldResponse()
        {
            using var fixture = new Fixture();
            var oldResponse = new TaskCompletionSource<VersionHandshakeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            fixture.Response = _ => Interlocked.Increment(ref calls) == 1
                ? oldResponse.Task
                : Task.FromResult(Compatible());
            fixture.TransportConnected.OnNext(Unit.Default);

            var newHub = new HubConnectionBuilder().WithUrl("http://localhost:9999/replacement").Build();
            fixture.Hub.Value = newHub;
            fixture.TransportConnected.OnNext(Unit.Default);
            oldResponse.SetResult(Compatible());
            await fixture.Connector.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            calls.ShouldBe(2);
            fixture.Manager.Verify(m => m.SetConnected(), Times.Once);
            await newHub.DisposeAsync();
        }

        sealed class Fixture : IDisposable
        {
            readonly CancellationTokenSource _cycle = new CancellationTokenSource();
            readonly ReactiveProperty<bool> _keepConnected = new ReactiveProperty<bool>(true);
            readonly ReactiveProperty<HubConnectionState> _state = new ReactiveProperty<HubConnectionState>(HubConnectionState.Connecting);
            readonly HubConnection _initialHub = new HubConnectionBuilder().WithUrl("http://localhost:9999/test").Build();
            readonly ReadOnlyReactiveProperty<bool> _keepConnectedReadOnly;
            readonly ReadOnlyReactiveProperty<HubConnectionState> _stateReadOnly;
            readonly ReadOnlyReactiveProperty<HubConnection?> _hubReadOnly;
            public readonly ReactiveProperty<HubConnection?> Hub;
            public readonly Subject<Unit> TransportConnected = new Subject<Unit>();
            public readonly Mock<IConnectionManager> Manager = new Mock<IConnectionManager>();
            public readonly TestConnector Connector;
            public Func<CancellationToken, Task<VersionHandshakeResponse>> Response = _ => Task.FromResult(Compatible());

            public Fixture()
            {
                Hub = new ReactiveProperty<HubConnection?>(_initialHub);
                _keepConnectedReadOnly = _keepConnected.ToReadOnlyReactiveProperty();
                _stateReadOnly = _state.ToReadOnlyReactiveProperty();
                _hubReadOnly = Hub.ToReadOnlyReactiveProperty();
                Manager.SetupGet(m => m.KeepConnected).Returns(_keepConnectedReadOnly);
                Manager.SetupGet(m => m.ConnectionState).Returns(_stateReadOnly);
                Manager.SetupGet(m => m.HubConnection).Returns(_hubReadOnly);
                Manager.SetupGet(m => m.ConnectionCancellationToken).Returns(() => _cycle.Token);
                Manager.SetupGet(m => m.OnTransportConnected).Returns(TransportConnected);
                Manager.SetupGet(m => m.Endpoint).Returns("http://localhost:9999/test");
                Manager.Setup(m => m.InvokeAsync<RequestVersionHandshake, VersionHandshakeResponse>(
                    nameof(IServerMcpManager.PerformVersionHandshake), It.IsAny<RequestVersionHandshake>(), It.IsAny<CancellationToken>()))
                    .Returns((string _, RequestVersionHandshake _, CancellationToken token) => Response(token));
                Manager.Setup(m => m.Disconnect(It.IsAny<CancellationToken>())).Returns(() =>
                {
                    _cycle.Cancel();
                    _keepConnected.Value = false;
                    return Task.CompletedTask;
                });
                Connector = new TestConnector(Manager.Object);
            }

            public void Dispose()
            {
                Connector.Dispose();
                _cycle.Dispose();
                TransportConnected.Dispose();
                _hubReadOnly.Dispose();
                _keepConnectedReadOnly.Dispose();
                _stateReadOnly.Dispose();
                Hub.Dispose();
                _keepConnected.Dispose();
                _state.Dispose();
                _initialHub.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        sealed class TestConnector : BaseHubConnector
        {
            public readonly TaskCompletionSource<bool> Connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> RetryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Func<CancellationToken, Task> Delay = token => Task.Delay(1, token);
            public TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

            public TestConnector(IConnectionManager manager)
                : base(NullLogger.Instance, new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" }, manager)
            {
            }

            protected override bool IsTransportConnected => true;
            protected override TimeSpan HandshakeAttemptTimeout => AttemptTimeout;
            protected override Task WaitBeforeHandshakeRetry(int failures, CancellationToken token)
            {
                RetryStarted.TrySetResult(true);
                return Delay(token);
            }
            protected override void SubscribeOnServerEvents(HubConnection hubConnection, CompositeDisposable disposables) { }
            protected override Task OnConnectedAsync(CancellationToken token)
            {
                Connected.TrySetResult(true);
                return Task.CompletedTask;
            }
        }
    }
}
