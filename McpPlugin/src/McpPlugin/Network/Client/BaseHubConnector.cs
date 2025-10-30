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
            _logger.LogTrace("{class} Connecting... to {endpoint}.", GetType().Name, _connectionManager.Endpoint);
            return _connectionManager.Connect(cancellationToken);
        }
        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Disconnecting... to {endpoint}.", GetType().Name, _connectionManager.Endpoint);
            return _connectionManager.Disconnect(cancellationToken);
        }

        private async void OnHubConnectionChanged(HubConnection? hubConnection)
        {
            _logger.LogTrace("{class} Clearing server events disposables.", GetType().Name);
            _serverEventsDisposables.Clear();

            if (hubConnection == null)
                return;

            // Perform version handshake first

            var cancellationToken = _serverEventsDisposables.ToCancellationToken();
            var handshakeResponse = await PerformVersionHandshake(
                request: new VersionHandshakeRequest
                {
                    RequestID = Guid.NewGuid().ToString(),
                    ApiVersion = _apiVersion.Api,
                    PluginVersion = _apiVersion.Plugin,
                    UnityVersion = _apiVersion.Environment
                },
                cancellationToken: cancellationToken);

            if (handshakeResponse != null && !handshakeResponse.Compatible)
            {
                LogVersionMismatchError(handshakeResponse);
                // Still proceed with tool notification for now, but user will see the error
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            _logger.LogTrace("{class} Subscribing to server events.", GetType().Name);
            SubscribeOnServerEvents(hubConnection, _serverEventsDisposables);
        }

        private void LogVersionMismatchError(VersionHandshakeResponse handshakeResponse)
        {
            var errorMessage = $"[MCP-Plugin] API VERSION MISMATCH: {handshakeResponse.Message}";

            // Log using ILogger which will be connected to Unity's logging system from the outside
            _logger.LogError(errorMessage);
        }

        protected abstract void SubscribeOnServerEvents(HubConnection hubConnection, CompositeDisposable disposables);

        public async Task<VersionHandshakeResponse?> PerformVersionHandshake(VersionHandshakeRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Performing version handshake.", GetType().Name);

            try
            {
                var response = await _connectionManager.InvokeAsync<VersionHandshakeRequest, VersionHandshakeResponse>(
                    nameof(IServerMcpManager.PerformVersionHandshake), request, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("{class} Version handshake cancelled.", GetType().Name);
                    return null;
                }

                if (response == null)
                {
                    _logger.LogError("{class} Version handshake failed: No response from server.", GetType().Name);
                    return null;
                }

                _logger.LogInformation("{class} Version handshake completed. Compatible: {Compatible}, Message: {Message}",
                    GetType().Name, response.Compatible, response.Message);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class} Version handshake failed: {Error}", GetType().Name, ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            DisposeAsync().Wait();
        }
        public Task DisposeAsync()
        {
            _logger.LogTrace("{class} DisposeAsync.", GetType().Name);
            _serverEventsDisposables.Dispose();
            _hubConnectionDisposable.Dispose();

            return _connectionManager.DisposeAsync();
        }

        ~BaseHubConnector()
        {
            DisposeAsync().Wait();
        }
    }
}
