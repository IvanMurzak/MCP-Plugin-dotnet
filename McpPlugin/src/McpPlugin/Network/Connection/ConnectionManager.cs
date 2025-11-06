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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin
{
    public class ConnectionManager : IConnectionManager
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

        private readonly ThreadSafeBool _isDisposed = new(false);
        private volatile Task<bool>? connectionTask;
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

            _logger.LogDebug("{class}[{guid}] Connect.", nameof(ConnectionManager), _guid);
            if (_hubConnection.Value?.State == HubConnectionState.Connected)
            {
                _logger.LogDebug("{class}[{guid}] Already connected. Ignoring.",
                    nameof(ConnectionManager), _guid);
                return true;
            }

            // Check for existing connection task FIRST before canceling or stopping
            var existingTask = connectionTask;
            if (existingTask != null)
            {
                _logger.LogDebug("{class}[{guid}] Connection task already exists. Waiting for the completion... {endpoint}.",
                    nameof(ConnectionManager), _guid, Endpoint);
                // Create a new task that waits for the existing task but can be canceled independently
                return await Task.Run(async () =>
                {
                    try
                    {
                        await existingTask; // Wait for the existing connection task
                        return _hubConnection.Value?.State == HubConnectionState.Connected;
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("{class}[{guid}] Connection task was canceled {endpoint}.",
                            nameof(ConnectionManager), _guid, Endpoint);
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("{class}[{guid}] Connection task failed: {message}",
                            nameof(ConnectionManager), _guid, ex.Message);
                        return false;
                    }
                }, cancellationToken);
            }

            _continueToReconnect.Value = false;

            // Dispose the previous internal CancellationTokenSource if it exists
            CancelInternalToken(dispose: true);

            if (_hubConnection.Value != null)
            {
                await _hubConnection.Value.StopAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("{class}[{guid}] Connection canceled before starting connection loop for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    return false;
                }
            }

            _continueToReconnect.Value = true;

            internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // Set the task BEFORE starting to prevent race conditions
                var taskCompletionSource = new TaskCompletionSource<bool>();
                var previousTask = Interlocked.CompareExchange(ref connectionTask, taskCompletionSource.Task, null);

                if (previousTask != null)
                {
                    // Another thread won the race, wait for their task
                    _logger.LogDebug("{class}[{guid}] Another connection attempt started concurrently. Waiting for it... {endpoint}.",
                        nameof(ConnectionManager), _guid, Endpoint);
                    return await Task.Run(async () =>
                    {
                        try
                        {
                            await previousTask;
                            return _hubConnection.Value?.State == HubConnectionState.Connected;
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("{class}[{guid}] Concurrent connection task was canceled {endpoint}.",
                                nameof(ConnectionManager), _guid, Endpoint);
                            return false;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("{class}[{guid}] Concurrent connection task failed: {message}",
                                nameof(ConnectionManager), _guid, ex.Message);
                            return false;
                        }
                    }, cancellationToken);
                }

                // We won the race, proceed with connection
                try
                {
                    var result = await InternalConnect(internalCts.Token);
                    taskCompletionSource.SetResult(result);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    // Ensure waiting tasks are notified of the cancellation
                    taskCompletionSource.TrySetCanceled();
                    _logger.LogWarning("{class}[{guid}] Connection was canceled for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    return false;
                }
                catch (Exception ex)
                {
                    // Ensure waiting tasks are notified of the failure
                    taskCompletionSource.TrySetResult(false);
                    _logger.LogError("{class}[{guid}] Error during connection: {message}\n{stackTrace}",
                        nameof(ConnectionManager), _guid, ex.Message, ex.StackTrace);
                    return false;
                }
            }
            finally
            {
                connectionTask = null;
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

        async Task<bool> InternalConnect(CancellationToken cancellationToken)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect));
                return false; // already disposed
            }

            _logger.LogTrace("{class}[{guid}] {method}",
                nameof(ConnectionManager), _guid, nameof(InternalConnect));

            if (!await CreateHubConnectionIfNeeded(cancellationToken))
                return false;

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] Connection canceled before starting connection loop for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] Connection canceled before starting connection loop for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                return false;
            }

            return await StartConnectionLoop(cancellationToken);
        }

        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
                return Task.CompletedTask; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] {method}.",
                 nameof(ConnectionManager), _guid, nameof(Disconnect));

            connectionTask = null;

            // Cancel the internal token to stop any ongoing connection attempts
            CancelInternalToken(dispose: false);
            _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            if (_hubConnection.Value == null)
                return Task.CompletedTask;

            return _hubConnection.Value.StopAsync(cancellationToken).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _logger.LogInformation("{class}[{guid}] HubConnection stopped successfully.",
                        nameof(ConnectionManager), _guid);
                }
                else if (task.Exception != null)
                {
                    _logger.LogError("{class}[{guid}] Error while stopping HubConnection: {message}\n{stackTrace}",
                        nameof(ConnectionManager), _guid, task.Exception.Message, task.Exception.StackTrace);
                }
                _connectionState.Value = HubConnectionState.Disconnected;
            });
        }

        public void Dispose()
        {
#pragma warning disable CS4014
            DisposeAsync();
#pragma warning restore CS4014
        }

        public async Task DisposeAsync()
        {
            if (!_isDisposed.TrySetTrue())
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(DisposeAsync));
                return; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] DisposeAsync.",
                nameof(ConnectionManager), _guid);

            _disposables.Dispose();
            connectionTask = null;

            if (!_continueToReconnect.IsDisposed)
                _continueToReconnect.Value = false;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            hubConnectionLogger = null;
            hubConnectionObservable = null;

            _connectionState.Dispose();
            _continueToReconnect.Dispose();

            CancelInternalToken(dispose: true);

            if (_hubConnection.CurrentValue != null)
            {
                try
                {
                    var tempHubConnection = _hubConnection.Value;

                    if (!_hubConnection.IsDisposed)
                        _hubConnection.Value = null;
                    _hubConnection.Dispose();

                    if (tempHubConnection != null)
                    {
                        await tempHubConnection.StopAsync()
                            .ContinueWith(task =>
                            {
                                try
                                {
                                    tempHubConnection.DisposeAsync();
                                }
                                catch { }
                            });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("{class}[{guid}] Error during async disposal: {message}\n{stackTrace}",
                        nameof(ConnectionManager), _guid, ex.Message, ex.StackTrace);
                }
            }

            if (!_hubConnection.IsDisposed)
                _hubConnection.Dispose();

            _logger.LogDebug("{class}[{guid}] DisposeAsync completed.",
                nameof(ConnectionManager), _guid);
        }

        // New helper methods for better separation of concerns
        private async Task<bool> EnsureConnection(CancellationToken cancellationToken)
        {
            if (_hubConnection.CurrentValue?.State == HubConnectionState.Connected)
                return true;

            if (!_continueToReconnect.CurrentValue)
            {
                _logger.LogWarning("{class}[{guid}] Connection not available and auto-reconnect disabled for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                return false;
            }

            _logger.LogDebug("{class}[{guid}] Connection is not established. Attempting to connect to: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);
            await Connect(cancellationToken);

            if (_hubConnection.CurrentValue?.State != HubConnectionState.Connected)
            {
                _logger.LogError("{class}[{guid}] Failed to establish connection to remote endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                return false;
            }

            return true;
        }

        private async Task ExecuteHubMethodAsync(string methodName, Func<HubConnection, Task> hubMethod)
        {
            if (_hubConnection.CurrentValue == null)
            {
                _logger.LogError("{class}[{guid}] HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint);
                return;
            }

            try
            {
                await hubMethod(_hubConnection.CurrentValue);
                _logger.LogInformation("{class}[{guid}] Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] Failed to invoke method '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint, ex.Message);
                throw;
            }
        }

        private async Task<TResult> ExecuteHubMethodAsync<TResult>(string methodName, Func<HubConnection, Task<TResult>> hubMethod)
        {
            if (_hubConnection.CurrentValue == null)
            {
                _logger.LogError("{class}[{guid}] HubConnection is null. Cannot invoke method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint);
                return default!;
            }

            try
            {
                var result = await hubMethod(_hubConnection.CurrentValue);
                _logger.LogInformation("{class}[{guid}] Successfully invoked method '{methodName}' on endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] Failed to invoke method '{methodName}' on endpoint: {endpoint}. Error: {message}",
                    nameof(ConnectionManager), _guid, methodName, Endpoint, ex.Message);
                return default!;
            }
        }

        private async Task<bool> CreateHubConnectionIfNeeded(CancellationToken cancellationToken)
        {
            if (_hubConnection.Value != null)
                return true;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();

            _logger.LogDebug("{class}[{guid}] Creating new HubConnection instance for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);

            var hubConnection = await _hubConnectionBuilder.CreateConnectionAsync(Endpoint);
            if (hubConnection == null)
            {
                _logger.LogError("{class}[{guid}] Failed to create HubConnection instance. Check connection configuration for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                return false;
            }

            _logger.LogDebug("{class}[{guid}] Successfully created HubConnection instance for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);
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
                .Subscribe(_ => connectionTask = null)
                .RegisterTo(cancellationToken);

            hubConnectionObservable.Closed
                .Where(_ => _continueToReconnect.CurrentValue)
                .Where(_ => !cancellationToken.IsCancellationRequested)
                .Subscribe(async _ =>
                {
                    _logger.LogWarning("{class}[{guid}] Connection closed unexpectedly. Attempting to reconnect to: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    await InternalConnect(cancellationToken);
                })
                .RegisterTo(cancellationToken);
        }

        private async Task<bool> StartConnectionLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug("{class}[{guid}] Starting connection loop for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);

            while (!cancellationToken.IsCancellationRequested && _continueToReconnect.CurrentValue)
            {
                if (await AttemptConnection(cancellationToken))
                    return true;

                await WaitBeforeRetry(cancellationToken);
            }

            _logger.LogWarning("{class}[{guid}] Connection loop terminated for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);
            return false;
        }

        private async Task<bool> AttemptConnection(CancellationToken cancellationToken)
        {
            var connection = _hubConnection.CurrentValue;
            if (connection == null)
                return false;

            _logger.LogInformation("{class}[{guid}] Starting connection attempt to: {endpoint}",
                nameof(ConnectionManager), _guid, Endpoint);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var connectionTask = connection.StartAsync(cts.Token);

            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var completedTask = await Task.WhenAny(connectionTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("{class}[{guid}] Connection attempt timed out after 30 seconds for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    return false;
                }

                if (connectionTask.IsCompletedSuccessfully)
                {
                    _logger.LogInformation("{class}[{guid}] Connection established successfully to: {endpoint}",
                        nameof(ConnectionManager), _guid, Endpoint);
                    _connectionState.Value = HubConnectionState.Connected;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{class}[{guid}] Connection attempt failed for endpoint: {endpoint}. Error: {error}",
                    nameof(ConnectionManager), _guid, Endpoint, ex.Message);
            }

            return false;
        }

        private async Task WaitBeforeRetry(CancellationToken cancellationToken)
        {
            if (_continueToReconnect.CurrentValue && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogTrace("{class}[{guid}] Waiting 5 seconds before retry for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, Endpoint);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        ~ConnectionManager() => Dispose();
    }
}
