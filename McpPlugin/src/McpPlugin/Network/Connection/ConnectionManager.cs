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
        private readonly Subject<Unit> _authorizationRejected = new();
        private readonly Subject<Unit> _transportConnected = new();
        protected readonly CompositeDisposable _disposables = new();
        protected readonly CancellationTokenSource _cancellationTokenSource;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly SemaphoreSlim _ongoingConnectionGate = new(1, 1);
        private readonly ThreadSafeBool _isDisposed = new(false);
        private readonly SerialDisposable _hubStateSubscription = new();
        private readonly SerialDisposable _hubObservableReconnectSubscription = new();
        private readonly ReadOnlyReactiveProperty<HubConnectionState> _connectionStateReadOnly;
        private readonly ReadOnlyReactiveProperty<HubConnection?> _hubConnectionReadOnly;
        private readonly ReadOnlyReactiveProperty<bool> _keepConnectedReadOnly;
        private HubConnectionLogger? hubConnectionLogger;
        private HubConnectionObservable? hubConnectionObservable;
        private CancellationTokenSource? internalCts;
        private volatile Task<bool>? _ongoingConnectionTask;

        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _connectionStateReadOnly;
        public ReadOnlyReactiveProperty<HubConnection?> HubConnection => _hubConnectionReadOnly;
        public ReadOnlyReactiveProperty<bool> KeepConnected => _keepConnectedReadOnly;
        public Observable<Unit> OnAuthorizationRejected => _authorizationRejected;
        public Observable<Unit> OnTransportConnected => _transportConnected;
        public string Endpoint => _endpoint;

        public void SetConnected()
        {
            if (_isDisposed.Value)
                return;
            _connectionState.Value = HubConnectionState.Connected;
        }

        public void NotifyAuthorizationRejected()
        {
            if (_isDisposed.Value)
                return;
            _authorizationRejected.OnNext(Unit.Default);
        }
        public CancellationToken ConnectionCancellationToken => internalCts?.Token ?? CancellationToken.None;

        public ConnectionManager(ILogger logger, Version apiVersion, string endpoint, IHubConnectionProvider hubConnectionBuilder)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class}[{guid}] Ctor.", nameof(ConnectionManager), _guid);

            _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _hubConnectionBuilder = hubConnectionBuilder ?? throw new ArgumentNullException(nameof(hubConnectionBuilder));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();

            _connectionStateReadOnly = _connectionState.ToReadOnlyReactiveProperty();
            _hubConnectionReadOnly = _hubConnection.ToReadOnlyReactiveProperty();
            _keepConnectedReadOnly = _continueToReconnect.ToReadOnlyReactiveProperty();

            _hubConnection
                .Subscribe(hubConnection =>
                {
                    if (hubConnection == null)
                    {
                        _hubStateSubscription.Disposable = null;
                        _connectionState.Value = HubConnectionState.Disconnected;
                        return;
                    }

                    // SerialDisposable auto-disposes the previous subscription when reassigned,
                    // preventing accumulation of stale subscriptions across reconnection cycles.
                    // Note: Connected state is excluded from auto-sync — it is only set
                    // after the application-level handshake succeeds (via SetConnected).
                    var hubConnectionObservable = hubConnection.ToObservable();
                    var stateSubscription = hubConnectionObservable.State
                        .Where(state => state != HubConnectionState.Connected)
                        .Subscribe(state => _connectionState.Value = state);
                    _hubStateSubscription.Disposable = new CompositeDisposable(hubConnectionObservable, stateSubscription);
                })
                .AddTo(_disposables);

            // Reconnection is handled by SetupHubConnectionObservables (Closed + Reconnected events).
            // We do NOT subscribe to Reconnecting here to avoid interfering with SignalR's auto-reconnect.
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

        public async Task<TResult> InvokeAsync<TResult>(string methodName, CancellationToken cancellationToken = default)
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
                hubConnection.InvokeAsync<TResult>(methodName, cancellationToken));
        }

        public void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            GC.SuppressFinalize(this);
            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(Dispose));

            DisposeCommonSync();

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

                try { _gate.Dispose(); } catch (ObjectDisposedException) { }
                try { _ongoingConnectionGate.Dispose(); } catch (ObjectDisposedException) { }

                _logger.LogDebug("{class}[{guid}] {method} completed.",
                    nameof(ConnectionManager), _guid, nameof(Dispose));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            GC.SuppressFinalize(this);
            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(DisposeAsync));

            DisposeCommonSync();

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

                try { _gate.Dispose(); } catch (ObjectDisposedException) { }
                try { _ongoingConnectionGate.Dispose(); } catch (ObjectDisposedException) { }

                _logger.LogDebug("{class}[{guid}] {method} completed.",
                    nameof(ConnectionManager), _guid, nameof(DisposeAsync));
            }
        }

        /// <summary>
        /// Shared synchronous teardown logic executed by both <see cref="Dispose"/> and <see cref="DisposeAsync"/>.
        /// Does not touch the HubConnection (handled differently per path).
        /// </summary>
        private void DisposeCommonSync()
        {
            CancelInternalToken(dispose: true);
            _disposables.Dispose();

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _hubStateSubscription.Dispose();
            _hubObservableReconnectSubscription.Dispose();
            _connectionState.Dispose();
            _continueToReconnect.Dispose();
            _connectionStateReadOnly.Dispose();
            _hubConnectionReadOnly.Dispose();
            _keepConnectedReadOnly.Dispose();
        }

        private async Task ExecuteHubMethodAsync(string methodName, Func<HubConnection, Task> hubMethod)
        {
            var connection = _hubConnection.CurrentValue;
            if (connection == null)
            {
                _logger.LogError("{class}[{guid}] {method} HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return;
            }

            if (connection.State != HubConnectionState.Connected)
            {
                _connectionState.Value = connection.State;
                _logger.LogWarning("{class}[{guid}] {method} HubConnection is not active (State: {state}). Skipping method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), connection.State, methodName, Endpoint);
                return;
            }

            try
            {
                await hubMethod(connection);
                _logger.LogDebug("{class}[{guid}] {method} Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not active"))
            {
                _connectionState.Value = connection.State;
                _logger.LogWarning("{class}[{guid}] {method} Connection became inactive while invoking '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint, ex.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Invocation of '{methodName}' was canceled on endpoint: {endpoint}",
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
            var connection = _hubConnection.CurrentValue;
            if (connection == null)
            {
                _logger.LogError("{class}[{guid}] {method} HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return default!;
            }

            if (connection.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("{class}[{guid}] {method} HubConnection is not active (State: {state}). Skipping method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), connection.State, methodName, Endpoint);
                return default!;
            }

            try
            {
                var result = await hubMethod(connection);
                _logger.LogDebug("{class}[{guid}] {method} Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return result;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not active"))
            {
                _connectionState.Value = connection.State;
                _logger.LogWarning("{class}[{guid}] {method} Connection became inactive while invoking '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint, ex.Message);
                return default!;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Invocation of '{methodName}' was canceled on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint);
                return default!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] {method} Failed to invoke method '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, nameof(ExecuteHubMethodAsync), methodName, Endpoint, ex.Message);
                throw;
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

    }
}
