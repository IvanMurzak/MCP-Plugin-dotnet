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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin
{
    public abstract class BaseHubConnector : IConnectServerHub, IConnectionRecovery, IDisposable
    {
        protected readonly ILogger _logger;
        protected readonly Version _apiVersion;
        protected readonly IConnectionManager _connectionManager;
        protected readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly ThreadSafeBool _isDisposed = new(false);
        private readonly ThreadSafeBool _handshakeInFlight = new(false);
        private volatile VersionHandshakeResponse? lastHandshakeResponse = null;
        private int _handshakePending;
        private CancellationToken _serverEventsToken;

        /// <summary>
        /// Disposable for subscription on the HubConnection changes.
        /// </summary>
        protected readonly IDisposable _hubConnectionDisposable;

        /// <summary>
        /// Disposables for subscription on the server events RPC calls.
        /// </summary>
        protected readonly CompositeDisposable _serverEventsDisposables = new();

        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _connectionManager.ConnectionState;
        public ReadOnlyReactiveProperty<bool> KeepConnected => _connectionManager.KeepConnected;
        public Observable<Unit> OnAuthorizationRejected => _connectionManager.OnAuthorizationRejected;
        public Observable<Unit> OnDisconnectRequested => (_connectionManager as IConnectionRecovery)?.OnDisconnectRequested
            ?? Observable.Empty<Unit>();
        public VersionHandshakeResponse? VersionHandshakeStatus => lastHandshakeResponse;

        /// <summary>
        /// Primary constructor. Accepts an already-constructed <see cref="IConnectionManager"/>,
        /// enabling injection of a mock or custom implementation in tests.
        /// </summary>
        public BaseHubConnector(ILogger logger, Version apiVersion, IConnectionManager connectionManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class} Ctor.", GetType().Name);

            _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

            var subscriptions = new CompositeDisposable();

            // Register/clear server event handlers when HubConnection is created/destroyed.
            // Handlers must be on the HubConnection BEFORE StartAsync so the server's
            // immediate post-connect messages have registered targets.
            _connectionManager.HubConnection
                .Subscribe(OnHubConnectionChanged)
                .AddTo(subscriptions);

            // Perform version handshake when the SignalR transport connects.
            // OnTransportConnected fires from AttemptConnection after StartAsync succeeds,
            // covering both initial connect and reconnect cases.
            _connectionManager.OnTransportConnected
                .Subscribe(_ => OnConnectionEstablished())
                .AddTo(subscriptions);

            _hubConnectionDisposable = subscriptions;
        }

        /// <summary>
        /// Convenience constructor that creates a <see cref="ConnectionManager"/> internally.
        /// </summary>
        public BaseHubConnector(ILogger logger, Version apiVersion, string endpoint, IHubConnectionProvider hubConnectionProvider, int maxConsecutiveConnectionFailures = 0)
            : this(logger, apiVersion, new ConnectionManager(
                logger ?? throw new ArgumentNullException(nameof(logger)),
                apiVersion ?? throw new ArgumentNullException(nameof(apiVersion)),
                endpoint ?? throw new ArgumentNullException(nameof(endpoint)),
                hubConnectionProvider ?? throw new ArgumentNullException(nameof(hubConnectionProvider)),
                maxConsecutiveConnectionFailures))
        {
        }

        public Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.", nameof(Connect));
                return Task.FromResult(false);
            }
            _logger.LogDebug("{method} Connecting... to {endpoint}.",
                nameof(Connect), _connectionManager.Endpoint);
            return _connectionManager.Connect(cancellationToken);
        }

        public Task<bool> ConnectForRecovery(CancellationToken cancellationToken)
            => _isDisposed.Value ? Task.FromResult(false)
                : (_connectionManager as IConnectionRecovery)?.ConnectForRecovery(cancellationToken)
                    ?? _connectionManager.Connect(cancellationToken);

        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.",
                    nameof(Disconnect));
                return Task.CompletedTask;
            }
            _logger.LogDebug("{method} Disconnecting... from {endpoint}.",
                nameof(Disconnect), _connectionManager.Endpoint);
            return _connectionManager.Disconnect(cancellationToken);
        }

        public void DisconnectImmediate()
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.",
                    nameof(DisconnectImmediate));
                return;
            }

            _logger.LogDebug("{method}... from {endpoint}.",
                nameof(DisconnectImmediate), _connectionManager.Endpoint);

            _connectionManager.DisconnectImmediate();
        }

        public bool WaitForImmediateTeardown(TimeSpan timeout)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.",
                    nameof(WaitForImmediateTeardown));
                return true;
            }

            return _connectionManager.WaitForImmediateTeardown(timeout);
        }

        public Task<VersionHandshakeResponse> PerformVersionHandshake(RequestVersionHandshake request) => PerformVersionHandshake(request, _cancellationTokenSource.Token);
        public async Task<VersionHandshakeResponse> PerformVersionHandshake(RequestVersionHandshake request, CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
                throw new ObjectDisposedException(GetType().Name, "Can't perform version handshake on disposed object.");

            _logger.LogTrace("{class} Performing version handshake.", GetType().Name);

            try
            {
                var response = await _connectionManager.InvokeAsync<RequestVersionHandshake, VersionHandshakeResponse>(
                    nameof(IServerMcpManager.PerformVersionHandshake), request, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("{class} Version handshake cancelled.", GetType().Name);
                    return new VersionHandshakeResponse
                    {
                        ApiVersion = "Unknown",
                        ServerVersion = "Unknown",
                        Compatible = false,
                        Message = "Version handshake was cancelled.",
                        IsConnectionError = true
                    };
                }

                if (response == null)
                {
                    _logger.LogError("{class} Version handshake failed: No response from server.", GetType().Name);
                    return new VersionHandshakeResponse
                    {
                        ApiVersion = "Unknown",
                        ServerVersion = "Unknown",
                        Compatible = false,
                        Message = "Version handshake failed with null response.",
                        IsConnectionError = true
                    };
                }

                _logger.LogInformation("{class} Version handshake completed. Compatible: {Compatible}, Message: {Message}",
                    GetType().Name, response.Compatible, response.Message);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class} Version handshake failed: {Error}", GetType().Name, ex.Message);
                return new VersionHandshakeResponse
                {
                    ApiVersion = "Unknown",
                    ServerVersion = "Unknown",
                    Compatible = false,
                    Message = "Version handshake failed with exception: " + ex.Message,
                    IsConnectionError = true
                };
            }
        }

        private void OnHubConnectionChanged(HubConnection? hubConnection)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.", nameof(OnHubConnectionChanged));
                return;
            }

            _logger.LogTrace("{method} Clearing server events disposables.",
                nameof(OnHubConnectionChanged));

            _serverEventsDisposables.Clear();

            if (hubConnection == null)
                return;

            var session = new CancellationTokenSource();
            _serverEventsToken = session.Token;
            _serverEventsDisposables.Add(Disposable.Create(() =>
            {
                session.Cancel();
                session.Dispose();
            }));
            OnBeforeSubscribeToServerEvents();

            // Register handlers BEFORE StartAsync so the server's immediate
            // post-connect messages have registered targets.
            _logger.LogTrace("{method} Subscribing to server events.",
                nameof(OnHubConnectionChanged));

            SubscribeOnServerEvents(hubConnection, _serverEventsDisposables);
        }

        private async void OnConnectionEstablished()
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called on disposed object. Ignoring.", nameof(OnConnectionEstablished));
                return;
            }

            Interlocked.Exchange(ref _handshakePending, 1);
            if (!_handshakeInFlight.TrySetTrue())
            {
                _logger.LogDebug("{method} Handshake already in flight; queued transport notification.", nameof(OnConnectionEstablished));
                return;
            }

            try
            {
                do
                {
                    Interlocked.Exchange(ref _handshakePending, 0);
                    await OnConnectionEstablishedCore();
                }
                while (!_isDisposed.Value && Volatile.Read(ref _handshakePending) != 0);
            }
            catch (OperationCanceledException)
            {
                // Manual stop, transport replacement and disposal end this handshake worker.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{method} Unhandled exception during connection establishment.", nameof(OnConnectionEstablished));
            }
            finally
            {
                _handshakeInFlight.TrySetFalse();
                // A transport event can arrive between the loop's final pending check and
                // releasing single-flight ownership. Hand that event to a fresh worker.
                if (!_isDisposed.Value && Interlocked.Exchange(ref _handshakePending, 0) != 0)
                    OnConnectionEstablished();
            }
        }

        private async Task OnConnectionEstablishedCore()
        {
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(
                _serverEventsToken, _cancellationTokenSource.Token, _connectionManager.ConnectionCancellationToken);
            var cancellationToken = handshakeCts.Token;
            var failures = 0;

            // Perform version handshake after handlers are registered
            while (!cancellationToken.IsCancellationRequested && _connectionManager.KeepConnected.CurrentValue)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(HandshakeAttemptTimeout);
                var handshakeResponse = await PerformVersionHandshake(
                    request: new RequestVersionHandshake
                    {
                        RequestID = Guid.NewGuid().ToString(),
                        ApiVersion = _apiVersion.Api,
                        PluginVersion = _apiVersion.Plugin,
                        Environment = _apiVersion.Environment
                    },
                    cancellationToken: attemptCts.Token);

                if (cancellationToken.IsCancellationRequested)
                    return;

                lastHandshakeResponse = handshakeResponse;

                if (handshakeResponse == null || handshakeResponse.IsConnectionError)
                {
                    _logger.LogWarning("{class} Version handshake temporarily failed; retrying while connected. Reason: {reason}",
                        GetType().Name, handshakeResponse?.Message ?? "No response from server");
                    if (!IsTransportConnected)
                        return; // a later transport-connected event owns the next handshake
                    await WaitBeforeHandshakeRetry(failures++, cancellationToken);
                    if (!IsTransportConnected)
                        return;
                    continue;
                }

                if (!handshakeResponse.Compatible)
                {
                    LogVersionMismatchError(handshakeResponse);
                    _logger.LogError("{class} Version mismatch — disconnecting. Server: {serverVersion}, API: {apiVersion}, Message: {message}",
                        GetType().Name, handshakeResponse.ServerVersion, handshakeResponse.ApiVersion, handshakeResponse.Message);
                    _connectionManager.DisconnectImmediate();
                    return;
                }

                _connectionManager.SetConnected();
                await OnConnectedAsync(cancellationToken);
                return;
            }
        }

        protected virtual bool IsTransportConnected
            => _connectionManager.HubConnection.CurrentValue?.State == HubConnectionState.Connected;

        protected virtual TimeSpan HandshakeAttemptTimeout => TimeSpan.FromSeconds(30);

        protected virtual Task WaitBeforeHandshakeRetry(int failures, CancellationToken cancellationToken)
        {
            var seconds = Math.Min(60, 5 * Math.Pow(2, Math.Min(failures, 4)));
            return Task.Delay(TimeSpan.FromSeconds(Math.Min(60, seconds * (0.8 + new Random().NextDouble() * 0.4))), cancellationToken);
        }

        private void LogVersionMismatchError(VersionHandshakeResponse handshakeResponse)
        {
            var errorMessage = $"API VERSION MISMATCH: {handshakeResponse.Message}";
            _logger.LogError(errorMessage);
        }

        /// <summary>
        /// Called once per connection cycle, right before <see cref="SubscribeOnServerEvents"/>.
        /// Override to reset per-connection state that must be clean before any server
        /// notifications can arrive (e.g. epoch counters, flags).
        /// </summary>
        protected virtual void OnBeforeSubscribeToServerEvents() { }

        protected abstract void SubscribeOnServerEvents(HubConnection hubConnection, CompositeDisposable disposables);

        /// <summary>
        /// Called once after a successful connection and version handshake.
        /// Override to perform post-connect initialization (e.g. fetching initial state).
        /// </summary>
        protected virtual Task OnConnectedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public virtual void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            GC.SuppressFinalize(this);
            _logger.LogDebug("{method} called.", nameof(Dispose));

            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();

            _cancellationTokenSource.Dispose();
            _serverEventsDisposables.Dispose();
            _hubConnectionDisposable.Dispose();

            _connectionManager.Dispose();

            _logger.LogDebug("{method} completed.", nameof(Dispose));
        }

    }
}
