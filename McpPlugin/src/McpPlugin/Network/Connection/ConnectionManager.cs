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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin
{
    public partial class ConnectionManager : IConnectionManager, IAsyncDisposable
    {
        protected readonly string _guid = Guid.NewGuid().ToString();
        protected readonly ILogger _logger;
        protected readonly Version _apiVersion;
        protected readonly string _endpoint;
        protected readonly IHubConnectionProvider _hubConnectionBuilder;
        protected readonly ReactiveProperty<bool> _continueToReconnect = new(false);
        protected readonly ReactiveProperty<HubConnection?> _hubConnection = new();
        protected readonly ReactiveProperty<HubConnectionState> _connectionState = new(HubConnectionState.Disconnected);
        protected readonly CompositeDisposable _disposables = new();
        protected readonly CancellationTokenSource _cancellationTokenSource;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly SemaphoreSlim _ongoingConnectionGate = new(1, 1);
        private readonly ThreadSafeBool _isDisposed = new(false);
        private HubConnectionLogger? hubConnectionLogger;
        private HubConnectionObservable? hubConnectionObservable;
        private CancellationTokenSource? internalCts;
        private volatile Task<bool>? _ongoingConnectionTask;

        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _connectionState.ToReadOnlyReactiveProperty();
        public ReadOnlyReactiveProperty<HubConnection?> HubConnection => _hubConnection.ToReadOnlyReactiveProperty();
        public ReadOnlyReactiveProperty<bool> KeepConnected => _continueToReconnect.ToReadOnlyReactiveProperty();
        public string Endpoint => _endpoint;
        public CancellationToken ConnectionCancellationToken => internalCts?.Token ?? CancellationToken.None;

        public ConnectionManager(ILogger logger, Version apiVersion, string endpoint, IHubConnectionProvider hubConnectionBuilder)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class}[{guid}] Ctor.", nameof(ConnectionManager), _guid);

            _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _hubConnectionBuilder = hubConnectionBuilder ?? throw new ArgumentNullException(nameof(hubConnectionBuilder));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();

            _hubConnection
                .Subscribe(hubConnection =>
                {
                    if (hubConnection == null)
                    {
                        _connectionState.Value = HubConnectionState.Disconnected;
                        return;
                    }

                    hubConnection.ToObservable().State
                        .Subscribe(state => _connectionState.Value = state)
                        .AddTo(_disposables);
                })
                .AddTo(_disposables);

            _connectionState
                .Where(state => state == HubConnectionState.Reconnecting && _continueToReconnect.CurrentValue)
                .Subscribe(async state =>
                {
                    _logger.LogInformation("{class}[{guid}] Connection state changed to Reconnecting. Initiating reconnection to: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    await Connect(_cancellationTokenSource.Token);
                })
                .AddTo(_disposables);
        }

        public async Task InvokeAsync<TInput>(string methodName, TInput input, CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(InvokeAsync));
                return; // already disposed
            }

            if (!await EnsureConnection(cancellationToken))
                return;

            await ExecuteHubMethodAsync(methodName, hubConnection =>
                hubConnection.InvokeAsync(methodName, input, cancellationToken));
        }

        public async Task<TResult> InvokeAsync<TInput, TResult>(string methodName, TInput input, CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(InvokeAsync));
                return default!; // already disposed
            }

            if (!await EnsureConnection(cancellationToken))
                return default!;

            return await ExecuteHubMethodAsync(methodName, hubConnection =>
                hubConnection.InvokeAsync<TResult>(methodName, input, cancellationToken));
        }

        public void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(Dispose));

            CancelInternalToken(dispose: true);
            _disposables.Dispose();

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _connectionState.Dispose();
            _continueToReconnect.Dispose();

            // Use Wait with timeout for synchronous disposal
            var acquiredGate = _gate.Wait(TimeSpan.FromSeconds(5));
            try
            {
                if (!acquiredGate)
                {
                    _logger.LogWarning("{class}[{guid}] {method} Could not acquire gate within timeout during Dispose. Proceeding with cleanup anyway.",
                        nameof(ConnectionManager), _guid, nameof(Dispose));
                }
                // Clear the hub connection reference
                if (!_hubConnection.IsDisposed)
                {
                    try
                    {
                        _hubConnection.Value = null;
                        _hubConnection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("{class}[{guid}] {method} Error during disposal: {message}",
                            nameof(ConnectionManager), _guid, nameof(Dispose), ex.Message);
                    }
                }
            }
            finally
            {
                if (acquiredGate)
                    _gate.Release();

                _gate.Dispose();
                _ongoingConnectionGate.Dispose();

                _logger.LogDebug("{class}[{guid}] {method} completed.",
                    nameof(ConnectionManager), _guid, nameof(Dispose));
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(DisposeAsync));

            CancelInternalToken(dispose: true);
            _disposables.Dispose();

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _connectionState.Dispose();
            _continueToReconnect.Dispose();

            var isGateAcquired = await _gate.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                if (_hubConnection.CurrentValue != null)
                {
                    try
                    {
                        // Gracefully stop the connection
                        await _hubConnection.CurrentValue.StopAsync();
                        await _hubConnection.CurrentValue.DisposeAsync();

                        // Clear the hub connection reference
                        if (!_hubConnection.IsDisposed)
                            _hubConnection.Value = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("{class}[{guid}] {method} Error during async disposal: {message}\n{stackTrace}",
                            nameof(ConnectionManager), _guid, nameof(DisposeAsync), ex.Message, ex.StackTrace);
                    }
                }

                if (!_hubConnection.IsDisposed)
                    _hubConnection.Dispose();
            }
            finally
            {
                if (isGateAcquired)
                    _gate.Release();

                _gate.Dispose();
                _ongoingConnectionGate.Dispose();

                _logger.LogDebug("{class}[{guid}] {method} completed.",
                    nameof(ConnectionManager), _guid, nameof(DisposeAsync));
            }
        }

        private async Task ExecuteHubMethodAsync(string methodName, Func<HubConnection, Task> hubMethod)
        {
            if (_hubConnection.CurrentValue == null)
            {
                _logger.LogError("{class}[{guid}] {method} HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return;
            }

            try
            {
                await hubMethod(_hubConnection.CurrentValue);
                _logger.LogInformation("{class}[{guid}] {method} Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] {method} Failed to invoke method '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint, ex.Message);
                throw;
            }
        }

        private async Task<TResult> ExecuteHubMethodAsync<TResult>(string methodName, Func<HubConnection, Task<TResult>> hubMethod)
        {
            if (_hubConnection.CurrentValue == null)
            {
                _logger.LogError("{class}[{guid}] {method} HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return default!;
            }

            try
            {
                var result = await hubMethod(_hubConnection.CurrentValue);
                _logger.LogInformation("{class}[{guid}] {method} Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] {method} Failed to invoke method '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint, ex.Message);
                return default!;
            }
        }

        void CancelInternalToken(bool dispose = false)
        {
            if (internalCts == null)
                return;

            if (!internalCts.IsCancellationRequested)
                internalCts.Cancel();

            if (dispose)
            {
                internalCts.Dispose();
                internalCts = null;
            }
        }

        ~ConnectionManager() => Dispose();
    }
}
