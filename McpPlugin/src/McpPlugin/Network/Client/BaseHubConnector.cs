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
    public abstract class BaseHubConnector : IConnectServerHub, IDisposable
    {
        protected readonly ILogger _logger;
        protected readonly Version _apiVersion;
        protected readonly IConnectionManager _connectionManager;
        protected readonly CancellationTokenSource _cancellationTokenSource = new();

        private const int MaxHandshakeFailures = 3;
        private readonly ThreadSafeBool _isDisposed = new(false);
        private volatile VersionHandshakeResponse? lastHandshakeResponse = null;
        private int _consecutiveHandshakeFailures;

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

            // Perform version handshake only after the SignalR connection is fully established.
            // We watch the HubConnection's own state (not _connectionState) because
            // _connectionState is only set to Connected after the handshake succeeds.
            var handshakeSubscription = new SerialDisposable();
            _connectionManager.HubConnection
                .Subscribe(hc =>
                {
                    if (hc == null)
                    {
                        handshakeSubscription.Disposable = null;
                        return;
                    }
                    handshakeSubscription.Disposable = hc.ToObservable().State
                        .Where(state => state == HubConnectionState.Connected)
                        .Subscribe(_ => OnConnectionEstablished());
                })
                .AddTo(subscriptions);
            subscriptions.Add(handshakeSubscription);

            _hubConnectionDisposable = subscriptions;
        }

        /// <summary>
        /// Convenience constructor that creates a <see cref="ConnectionManager"/> internally.
        /// </summary>
        public BaseHubConnector(ILogger logger, Version apiVersion, string endpoint, IHubConnectionProvider hubConnectionProvider)
            : this(logger, apiVersion, new ConnectionManager(
                logger ?? throw new ArgumentNullException(nameof(logger)),
                apiVersion ?? throw new ArgumentNullException(nameof(apiVersion)),
                endpoint ?? throw new ArgumentNullException(nameof(endpoint)),
                hubConnectionProvider ?? throw new ArgumentNullException(nameof(hubConnectionProvider))))
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

            _consecutiveHandshakeFailures = 0;

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

            var serverEventsCts = _serverEventsDisposables.ToCancellationTokenSource();
            var cancellationToken = serverEventsCts.Token;

            // Perform version handshake after handlers are registered
            var handshakeResponse = await PerformVersionHandshake(
                request: new RequestVersionHandshake
                {
                    RequestID = Guid.NewGuid().ToString(),
                    ApiVersion = _apiVersion.Api,
                    PluginVersion = _apiVersion.Plugin,
                    Environment = _apiVersion.Environment
                },
                cancellationToken: cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            lastHandshakeResponse = handshakeResponse;

            if (handshakeResponse == null || handshakeResponse.IsConnectionError)
            {
                _consecutiveHandshakeFailures++;
                var reason = handshakeResponse?.Message ?? "No response from server";
                _logger.LogWarning("{class} Version handshake failed ({count}/{max}). Reason: {reason}",
                    GetType().Name, _consecutiveHandshakeFailures, MaxHandshakeFailures, reason);

                if (_consecutiveHandshakeFailures >= MaxHandshakeFailures)
                {
                    _logger.LogError("{class} Version handshake failed {count} times consecutively — disconnecting. Reason: {reason}",
                        GetType().Name, _consecutiveHandshakeFailures, reason);
                    _connectionManager.DisconnectImmediate();
                }
                return;
            }

            if (!handshakeResponse.Compatible)
            {
                LogVersionMismatchError(handshakeResponse);
                _logger.LogError("{class} Version mismatch — disconnecting. Server: {serverVersion}, API: {apiVersion}, Message: {message}",
                    GetType().Name, handshakeResponse.ServerVersion, handshakeResponse.ApiVersion, handshakeResponse.Message);
                _connectionManager.DisconnectImmediate();
                return;
            }

            _consecutiveHandshakeFailures = 0;
            _connectionManager.SetConnected();
            await OnConnectedAsync(cancellationToken);
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
