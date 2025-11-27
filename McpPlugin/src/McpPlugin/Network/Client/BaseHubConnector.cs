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

        private readonly ThreadSafeBool _isDisposed = new(false);

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

        public BaseHubConnector(ILogger logger, Version apiVersion, string endpoint, IHubConnectionProvider hubConnectionProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class} Ctor.", GetType().Name);

            _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));

            _connectionManager = new ConnectionManager(
                logger,
                apiVersion,
                endpoint ?? throw new ArgumentNullException(nameof(endpoint)),
                hubConnectionProvider ?? throw new ArgumentNullException(nameof(hubConnectionProvider))
            );

            _hubConnectionDisposable = _connectionManager.HubConnection
                .Subscribe(OnHubConnectionChanged);
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
                        Message = "Version handshake was cancelled."
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
                        Message = "Version handshake failed with null response."
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
                    Message = "Version handshake failed with exception: " + ex.Message
                };
            }
        }

        private async void OnHubConnectionChanged(HubConnection? hubConnection)
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
                return; // not connected

            var serverEventsCts = _serverEventsDisposables.ToCancellationTokenSource();
            var cancellationToken = serverEventsCts.Token;

            // Subscribe to server events BEFORE handshake to avoid race condition.
            // The server may send RunListTool/RunListPrompts immediately after handshake,
            // so handlers must be registered before we respond to the handshake.
            _logger.LogTrace("{method} Subscribing to server events (before handshake).",
                nameof(OnHubConnectionChanged));

            SubscribeOnServerEvents(hubConnection, _serverEventsDisposables);

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

            if (handshakeResponse != null && !handshakeResponse.Compatible)
            {
                LogVersionMismatchError(handshakeResponse);
                // Still proceed with tool notification for now, but user will see the error
            }
        }

        private void LogVersionMismatchError(VersionHandshakeResponse handshakeResponse)
        {
            var errorMessage = $"API VERSION MISMATCH: {handshakeResponse.Message}";
            _logger.LogError(errorMessage);
        }

        protected abstract void SubscribeOnServerEvents(HubConnection hubConnection, CompositeDisposable disposables);

        public virtual void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{method} called.", nameof(Dispose));

            if (!_cancellationTokenSource.IsCancellationRequested)
                _cancellationTokenSource.Cancel();

            _cancellationTokenSource.Dispose();
            _serverEventsDisposables.Dispose();
            _hubConnectionDisposable.Dispose();

            _connectionManager.Dispose();

            _logger.LogDebug("{method} completed.", nameof(Dispose));
        }

        ~BaseHubConnector() => Dispose();
    }
}
