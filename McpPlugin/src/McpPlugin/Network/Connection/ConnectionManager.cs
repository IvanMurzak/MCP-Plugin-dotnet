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
    public class ConnectionManager : IConnectionManager, IAsyncDisposable
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
        private readonly ThreadSafeBool _isDisposed = new(false);
        private HubConnectionLogger? hubConnectionLogger;
        private HubConnectionObservable? hubConnectionObservable;
        private CancellationTokenSource? internalCts;

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

        public async Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(Connect));
                return false; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(Connect));

            try
            {
                await _gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled while waiting for gate for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(Connect), Endpoint);
                return false;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("{class}[{guid}] {method} Connection canceled before starting for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(Connect), Endpoint);
                    return false;
                }

                if (_isDisposed.Value)
                {
                    _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                        nameof(ConnectionManager), _guid, nameof(Connect));
                    return false; // already disposed
                }

                if (_hubConnection.CurrentValue?.State is HubConnectionState.Connected or HubConnectionState.Connecting)
                {
                    _logger.LogDebug("{class}[{guid}] {method} Already connected. Ignoring.",
                        nameof(ConnectionManager), _guid, nameof(Connect));
                    return true;
                }

                _continueToReconnect.Value = false;

                // Dispose the previous internal CancellationTokenSource if it exists
                CancelInternalToken(dispose: true);

                internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = internalCts.Token;

                _continueToReconnect.Value = true;

                try
                {
                    var result = await InternalConnect(cancellationToken);
                    _logger.LogDebug("{class}[{guid}] {method} completed with result: {result}.",
                        nameof(ConnectionManager), _guid, nameof(Connect), result);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("{class}[{guid}] {method} Connection was canceled for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(Connect), Endpoint);
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError("{class}[{guid}] {method} Error during connection: {message}\n{stackTrace}",
                        nameof(ConnectionManager), _guid, nameof(Connect), ex.Message, ex.StackTrace);
                    return false;
                }
            }
            finally
            {
                _logger.LogDebug("{class}[{guid}] {method} releasing gate.",
                    nameof(ConnectionManager), _guid, nameof(Connect));
                _gate.Release();
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

        /// <summary>
        /// Internal connection logic. Must be called from within a _gate-protected section.
        /// </summary>
        async Task<bool> InternalConnect(CancellationToken cancellationToken)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect));
                return false; // already disposed
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled before creating HubConnection for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), Endpoint);
                return false;
            }

            _logger.LogDebug("{class}[{guid}] {method}",
                nameof(ConnectionManager), _guid, nameof(InternalConnect));

            if (!await CreateHubConnectionIfNeeded(cancellationToken))
                return false;

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled before starting connection loop for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), Endpoint);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled before starting connection loop for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), Endpoint);
                return false;
            }

            return await StartConnectionLoop(cancellationToken);
        }

        public async Task Disconnect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
                return; // already disposed
            }
            try
            {
                await _gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} canceled while waiting for gate for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(Disconnect), Endpoint);
                return;
            }

            try
            {
                await DisconnectInternal(cancellationToken, graceful: true);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Immediately disconnects without waiting for async cleanup.
        /// Use this during assembly reload or other critical shutdown scenarios.
        /// </summary>
        public void DisconnectImmediate()
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(DisconnectImmediate));
                return; // already disposed
            }
            // Try to acquire gate for thread safety, but don't wait long during emergency shutdown
            var acquiredGate = _gate.Wait(TimeSpan.FromSeconds(1));
            try
            {
                _logger.LogDebug("{class}[{guid}] {method} Gate acquired: {acquired}",
                    nameof(ConnectionManager), _guid, nameof(DisconnectImmediate), acquiredGate);

                DisconnectInternal(CancellationToken.None, graceful: false).GetAwaiter().GetResult();
            }
            finally
            {
                if (acquiredGate)
                {
                    _logger.LogDebug("{class}[{guid}] {method} Releasing gate.",
                        nameof(ConnectionManager), _guid, nameof(DisconnectImmediate));
                    _gate.Release();
                }
                else
                {
                    _logger.LogWarning("{class}[{guid}] {method} Could not acquire gate within timeout. Proceeding without gate protection.",
                        nameof(ConnectionManager), _guid, nameof(DisconnectImmediate));
                }
            }
        }

        private async Task DisconnectInternal(CancellationToken cancellationToken, bool graceful)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(DisconnectInternal));
                return; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] {method}. Graceful: {graceful}",
                 nameof(ConnectionManager), _guid, nameof(DisconnectInternal), graceful);

            // Cancel the internal token to stop any ongoing connection attempts
            CancelInternalToken(dispose: false);
            _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            // Update state immediately to prevent reconnection attempts
            _connectionState.Value = HubConnectionState.Disconnected;

            var tempHubConnection = _hubConnection.CurrentValue;
            if (tempHubConnection == null)
                return;

            _hubConnection.Value = null;

            // For non-graceful disconnect (Unity domain reload), skip all async operations
            if (!graceful)
            {
                _logger.LogDebug("{class}[{guid}] {method} Performing immediate disconnect without waiting for cleanup.",
                    nameof(ConnectionManager), _guid, nameof(DisconnectInternal));

                _ = tempHubConnection.DisposeAsync();
                // Don't call StopAsync or DisposeAsync - they use thread pool during shutdown
                // Just clear the reference and let the connection die
                // The connection state was already set to Disconnected above
                return;
            }

            // For graceful disconnect, use proper async cleanup
            await DisconnectGracefulAsync(tempHubConnection, cancellationToken);
        }

        private async Task DisconnectGracefulAsync(HubConnection hubConnection, CancellationToken cancellationToken)
        {
            try
            {
                await hubConnection.StopAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("{class}[{guid}] {method} HubConnection stopped successfully.",
                    nameof(ConnectionManager), _guid, nameof(DisconnectGracefulAsync));
            }
            catch (Exception ex)
            {
                _logger.LogError("{class}[{guid}] {method} Error while stopping HubConnection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(DisconnectGracefulAsync), ex.Message, ex.StackTrace);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(Dispose));

            _disposables.Dispose();

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _connectionState.Dispose();
            _continueToReconnect.Dispose();

            CancelInternalToken(dispose: true);

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
            }

            _gate.Dispose();

            _logger.LogDebug("{class}[{guid}] {method} completed.",
                nameof(ConnectionManager), _guid, nameof(Dispose));
        }

        public async ValueTask DisposeAsync()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{class}[{guid}] {method}.",
                nameof(ConnectionManager), _guid, nameof(DisposeAsync));

            _disposables.Dispose();

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _connectionState.Dispose();
            _continueToReconnect.Dispose();

            CancelInternalToken(dispose: true);

            await _gate.WaitAsync();
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
                _gate.Release();
                _gate.Dispose();
            }

            _logger.LogDebug("{class}[{guid}] {method} completed.",
                nameof(ConnectionManager), _guid, nameof(DisposeAsync));
        }

        // New helper methods for better separation of concerns
        private async Task<bool> EnsureConnection(CancellationToken cancellationToken)
        {
            if (_hubConnection.CurrentValue?.State is HubConnectionState.Connected)
                return true;

            if (!_continueToReconnect.CurrentValue)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection not available and auto-reconnect disabled for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(EnsureConnection), Endpoint);
                return false;
            }

            _logger.LogDebug("{class}[{guid}] {method} Connection is not established. Attempting to connect to: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(EnsureConnection), Endpoint);

            await Connect(cancellationToken);

            if (_hubConnection.CurrentValue?.State is not HubConnectionState.Connected)
            {
                _logger.LogError("{class}[{guid}] {method} Failed to establish connection to remote endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(EnsureConnection), Endpoint);
                return false;
            }

            return true;
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

        /// <summary>
        /// Creates a HubConnection if needed. Must be called from within a _gate-protected section.
        /// </summary>
        private async Task<bool> CreateHubConnectionIfNeeded(CancellationToken cancellationToken)
        {
            if (_hubConnection.Value != null)
                return true;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            _logger.LogDebug("{class}[{guid}] {method} Creating new HubConnection instance for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded), Endpoint);

            var hubConnection = await _hubConnectionBuilder.CreateConnectionAsync(Endpoint);
            if (hubConnection == null)
            {
                _logger.LogError("{class}[{guid}] {method} Failed to create HubConnection instance. Check connection configuration for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded), Endpoint);
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled before setting up HubConnection for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded), Endpoint);

                try
                {
                    await hubConnection.DisposeAsync();
                }
                catch
                {
                    // Ignore exceptions during dispose
                }
                return false;
            }

            _logger.LogDebug("{class}[{guid}] {method} Successfully created HubConnection instance for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded), Endpoint);
            _hubConnection.Value = hubConnection;

            SetupHubConnectionLogging(hubConnection);
            SetupHubConnectionObservables(hubConnection, cancellationToken);

            return true;
        }

        private void SetupHubConnectionLogging(HubConnection hubConnection)
        {
            hubConnectionLogger = new(_logger, hubConnection, guid: _guid);
        }

        private void SetupHubConnectionObservables(HubConnection hubConnection, CancellationToken cancellationToken)
        {
            hubConnectionObservable = new(hubConnection);
            hubConnectionObservable.Closed
                .Where(_ => _continueToReconnect.CurrentValue)
                .Where(_ => !cancellationToken.IsCancellationRequested)
                .Subscribe(async _ =>
                {
                    _logger.LogWarning("{class}[{guid}] {method} Connection closed unexpectedly. Attempting to reconnect to: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(SetupHubConnectionObservables), Endpoint);
                    // Call Connect instead of InternalConnect to ensure proper gate protection
                    await Connect(cancellationToken);
                })
                .RegisterTo(cancellationToken);
        }

        /// <summary>
        /// Starts the connection retry loop. Must be called from within a _gate-protected section.
        /// </summary>
        private async Task<bool> StartConnectionLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug("{class}[{guid}] {method} Starting connection loop for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint);

            while (!cancellationToken.IsCancellationRequested && _continueToReconnect.CurrentValue)
            {
                if (await AttemptConnection(cancellationToken))
                    return true;

                await WaitBeforeRetry(cancellationToken);
            }

            _logger.LogWarning("{class}[{guid}] {method} Connection loop terminated for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint);
            return false;
        }

        /// <summary>
        /// Attempts to start the connection. Must be called from within a _gate-protected section.
        /// </summary>
        private async Task<bool> AttemptConnection(CancellationToken cancellationToken)
        {
            var connection = _hubConnection.CurrentValue;
            if (connection == null)
                return false;

            _logger.LogInformation("{class}[{guid}] {method} Starting connection attempt to: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint);

            try
            {
                var connectionTask = connection.StartAsync(cancellationToken);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var completedTask = await Task.WhenAny(connectionTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("{class}[{guid}] {method} Connection attempt timed out after 30 seconds for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint);
                    return false;
                }

                if (connectionTask.IsCompletedSuccessfully)
                {
                    _logger.LogInformation("{class}[{guid}] {method} Connection established successfully to: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint);
                    _connectionState.Value = HubConnectionState.Connected;
                    return true;
                }
                else
                {
                    _logger.LogWarning("{class}[{guid}] {method} Connection attempt failed for endpoint: {endpoint}. Exception: {exception}",
                        nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint, connectionTask.Exception?.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{class}[{guid}] {method} Connection attempt failed for endpoint: {endpoint}. Error: {error}",
                    nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint, ex.Message);
            }

            return false;
        }

        private async Task WaitBeforeRetry(CancellationToken cancellationToken)
        {
            if (_continueToReconnect.CurrentValue && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogTrace("{class}[{guid}] {method} Waiting 5 seconds before retry for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(WaitBeforeRetry), Endpoint);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        ~ConnectionManager() => Dispose();
    }
}
