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
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public partial class ConnectionManager : IConnectionManager, IAsyncDisposable
    {
        public async Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(Connect));
                return false; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] {method} called.",
                nameof(ConnectionManager), _guid, nameof(Connect));

            try
            {
                _logger.LogDebug("{class}[{guid}] {method} acquiring gate.",
                    nameof(ConnectionManager), _guid, nameof(Connect));

                await _gate.WaitAsync(cancellationToken);

                _logger.LogDebug("{class}[{guid}] {method} acquired gate.",
                    nameof(ConnectionManager), _guid, nameof(Connect));
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

                // Check for ongoing task after acquiring gate
                var ongoingTask = _ongoingConnectionTask;
                if (ongoingTask != null)
                {
                    _logger.LogDebug("{class}[{guid}] {method} Connection already in progress after acquiring gate, releasing gate and waiting.",
                        nameof(ConnectionManager), _guid, nameof(Connect));
                    _gate.Release();
                    return await WaitForConnectionCompletion(ongoingTask, cancellationToken);
                }

                if (_hubConnection.CurrentValue?.State is HubConnectionState.Connected or HubConnectionState.Connecting)
                {
                    _logger.LogDebug("{class}[{guid}] {method} Already connected. Ignoring.",
                        nameof(ConnectionManager), _guid, nameof(Connect));
                    return true;
                }

                // Check if the internal token was canceled by Disconnect before we acquired the gate
                // If it was, we should not start a new connection
                if (internalCts != null && internalCts.IsCancellationRequested)
                {
                    _logger.LogDebug("{class}[{guid}] {method} Internal token was canceled before starting connection, aborting for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(Connect), Endpoint);
                    return false;
                }

                _continueToReconnect.Value = false;

                // Dispose the previous internal CancellationTokenSource if it exists
                CancelInternalToken(dispose: true);

                internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = internalCts.Token;

                _continueToReconnect.Value = true;

                _ongoingConnectionTask = InternalConnect(cancellationToken);
                try
                {
                    return await _ongoingConnectionTask;
                }
                finally
                {
                    _ongoingConnectionTask = null;
                }
            }
            finally
            {
                _logger.LogDebug("{class}[{guid}] {method} releasing gate.",
                    nameof(ConnectionManager), _guid, nameof(Connect));
                _gate.Release();
            }
        }

        private async Task<bool> WaitForConnectionCompletion(Task<bool> ongoingTask, CancellationToken cancellationToken)
        {
            _logger.LogDebug("{class}[{guid}] {method} Connection already in progress, waiting for existing attempt.",
                nameof(ConnectionManager), _guid, nameof(WaitForConnectionCompletion));
            try
            {
                var completedTask = await Task.WhenAny(ongoingTask, Task.Delay(Timeout.Infinite, cancellationToken));
                if (completedTask != ongoingTask)
                {
                    _logger.LogWarning("{class}[{guid}] {method} Waiting for ongoing connection was canceled for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(WaitForConnectionCompletion), Endpoint);
                    return false;
                }
                return await ongoingTask;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Ongoing connection was canceled for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(WaitForConnectionCompletion), Endpoint);
                return false;
            }
        }

        /// <summary>
        /// Internal connection logic. Must be called from within a _gate-protected section.
        /// </summary>
        private async Task<bool> InternalConnect(CancellationToken cancellationToken)
        {
            try
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

                _logger.LogDebug("{class}[{guid}] {method} called.",
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
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection was canceled for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), Endpoint);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError("{class}[{guid}] {method} Invalid operation during connection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), ex.Message, ex.StackTrace);
                return false;
            }
            catch (HubException ex)
            {
                _logger.LogError("{class}[{guid}] {method} SignalR HubException during connection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), ex.Message, ex.StackTrace);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError("{class}[{guid}] {method} Unexpected error during connection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(InternalConnect), ex.Message, ex.StackTrace);
                return false;
            }
        }

        // Helper methods for better separation of concerns
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

            if (connection.State is HubConnectionState.Connected or HubConnectionState.Connecting)
                return true;

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
            catch (OperationCanceledException)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection attempt canceled for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint);
            }
            catch (HubException ex)
            {
                _logger.LogError("{class}[{guid}] {method} SignalR HubException during connection attempt to endpoint: {endpoint}. Error: {error}",
                    nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class}[{guid}] {method} Connection attempt failed for endpoint: {endpoint}. Error: {error}",
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
    }
}
