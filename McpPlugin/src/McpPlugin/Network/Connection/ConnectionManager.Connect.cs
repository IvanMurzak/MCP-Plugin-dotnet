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

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} Connection canceled before starting for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(Connect), Endpoint);
                return false;
            }

            // Check if there's already an ongoing connection attempt
            await _ongoingConnectionGate.WaitAsync(cancellationToken);
            var ongoingTask = _ongoingConnectionTask;
            _ongoingConnectionGate.Release();

            if (ongoingTask != null)
                return await WaitForConnectionCompletion(ongoingTask, cancellationToken);

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

                // Double-check for ongoing task after acquiring gate
                await _ongoingConnectionGate.WaitAsync(cancellationToken);
                ongoingTask = _ongoingConnectionTask;
                _ongoingConnectionGate.Release();

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

                // Dispose the previous internal CancellationTokenSource if it exists
                CancelInternalToken(dispose: true);

                internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cancellationToken = internalCts.Token;

                _continueToReconnect.Value = true;

                Task<bool> connectionTask;
                await _ongoingConnectionGate.WaitAsync(cancellationToken);
                connectionTask = InternalConnect(cancellationToken); // local ref first — never null
                _ongoingConnectionTask = connectionTask;             // publish to shared field under gate
                _ongoingConnectionGate.Release();
                try
                {
                    return await connectionTask; // safe: local ref was captured before the gate was released
                }
                finally
                {
                    // Use CancellationToken.None: cleanup must run even when cancellationToken
                    // (the internal CTS token) has already been cancelled by DisconnectImmediate.
                    await _ongoingConnectionGate.WaitAsync(CancellationToken.None);
                    _ongoingConnectionTask = null;
                    _ongoingConnectionGate.Release();
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

                // Small stabilization delay after HubConnection creation before the first StartAsync call.
                // SignalR's internal DI/setup may not be fully ready immediately after CreateConnectionAsync returns.
                // TODO: remove this delay if integration tests confirm it is no longer needed.
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
                _logger.LogWarning("{class}[{guid}] {method} Failed to establish connection to remote endpoint: {endpoint}",
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
            var existing = _hubConnection.Value;
            if (existing != null && existing.State != HubConnectionState.Disconnected)
                return true;

            hubConnectionLogger?.Dispose();
            hubConnectionObservable?.Dispose();
            _hubObservableReconnectSubscription.Disposable = null;

            // After SignalR auto-reconnect exhausts retries, the HubConnection is stuck in
            // Disconnected state. Dispose it so a fresh transport handshake can succeed.
            if (existing != null)
            {
                _logger.LogDebug("{class}[{guid}] {method} Disposing existing Disconnected HubConnection for endpoint: {endpoint}",
                    nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded), Endpoint);
                try { await existing.DisposeAsync(); }
                catch (Exception ex) { _logger.LogDebug(ex, "{class}[{guid}] {method} DisposeAsync of stale HubConnection failed", nameof(ConnectionManager), _guid, nameof(CreateHubConnectionIfNeeded)); }
                _hubConnection.Value = null;
            }

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
            SetupHubConnectionObservables(hubConnection);

            return true;
        }

        private void SetupHubConnectionLogging(HubConnection hubConnection)
        {
            hubConnectionLogger = new(_logger, hubConnection, guid: _guid);
        }

        private void SetupHubConnectionObservables(HubConnection hubConnection)
        {
            hubConnectionObservable = new(hubConnection);

            // On successful auto-reconnect, re-trigger the version handshake.
            // Guard against a race where Reconnected fires while a disconnect is in progress.
            var reconnectedSub = hubConnectionObservable.Reconnected
                .Where(_ => _continueToReconnect.CurrentValue && !_cancellationTokenSource.IsCancellationRequested)
                .Subscribe(_ =>
                {
                    _logger.LogInformation("{class}[{guid}] {method} SignalR auto-reconnect succeeded for endpoint: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(SetupHubConnectionObservables), Endpoint);
                    _transportConnected.OnNext(Unit.Default);
                });

            // On auto-reconnect exhaustion, create a fresh HubConnection and restart.
            // Uses _cancellationTokenSource (object lifetime) to avoid self-cancellation
            // when Connect() replaces the connection-cycle internalCts.
            var closedSub = hubConnectionObservable.Closed
                .Where(_ => _continueToReconnect.CurrentValue && !_cancellationTokenSource.IsCancellationRequested)
                .Subscribe(ex =>
                {
                    _logger.LogWarning(ex, "{class}[{guid}] {method} Connection closed (auto-reconnect exhausted). Attempting fresh reconnection to: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(SetupHubConnectionObservables), Endpoint);
                    // Fire-and-forget: Connect() handles sequential execution via its internal gate.
                    var reconnectTask = Connect(_cancellationTokenSource.Token);
                    _ = reconnectTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.ExecuteSynchronously);
                });

            _hubObservableReconnectSubscription.Disposable = new CompositeDisposable(reconnectedSub, closedSub);
        }

        /// <summary>
        /// Maximum number of consecutive immediate disconnects (server closes connection right after
        /// handshake) before the connection loop gives up. This pattern typically indicates that the
        /// server is rejecting the client (e.g. invalid or revoked authorization token).
        /// </summary>
        private const int MaxConsecutiveRejections = 3;

        /// <summary>
        /// If the server closes the connection within this duration after a successful handshake,
        /// it is counted as an immediate rejection (e.g. authorization failure).
        /// Protected to allow test subclasses to reduce the delay.
        /// </summary>
        protected virtual TimeSpan RejectionThreshold { get; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Starts the connection retry loop. Must be called from within a _gate-protected section.
        /// Detects server-side rejection patterns (immediate disconnect after handshake) and stops
        /// retrying after <see cref="MaxConsecutiveRejections"/> consecutive rejections.
        /// </summary>
        private async Task<bool> StartConnectionLoop(CancellationToken cancellationToken)
        {
            _logger.LogDebug("{class}[{guid}] {method} Starting connection loop for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint);

            var consecutiveRejections = 0;

            while (!cancellationToken.IsCancellationRequested && _continueToReconnect.CurrentValue)
            {
                if (await AttemptConnection(cancellationToken))
                {
                    // Connection established — verify the server doesn't immediately close it.
                    // A server-side authorization rejection typically closes the WebSocket within
                    // milliseconds of the handshake completing.
                    try
                    {
                        await Task.Delay(RejectionThreshold, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (_hubConnection.CurrentValue?.State is HubConnectionState.Connected)
                    {
                        // SignalR connection survived the stability check — it's genuinely established.
                        // Note: _connectionState is NOT set to Connected here. That happens only
                        // after the application-level handshake succeeds (via SetConnected).
                        consecutiveRejections = 0;
                        return true;
                    }

                    // Server closed the connection immediately after handshake.
                    _connectionState.Value = HubConnectionState.Disconnected;
                    consecutiveRejections++;
                    _logger.LogWarning("{class}[{guid}] {method} Connection to {endpoint} was closed by the server immediately after handshake ({count}/{max}). " +
                        "This typically indicates authorization failure (invalid or revoked token).",
                        nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint, consecutiveRejections, MaxConsecutiveRejections);

                    if (consecutiveRejections >= MaxConsecutiveRejections)
                    {
                        _logger.LogError("{class}[{guid}] {method} Connection to {endpoint} rejected {count} times consecutively. " +
                            "Stopping reconnection attempts. The server is likely rejecting this client due to an authorization issue. " +
                            "Please check your authorization token and try reconnecting.",
                            nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint, consecutiveRejections);
                        _continueToReconnect.Value = false;
                        _connectionState.Value = HubConnectionState.Disconnected;
                        _authorizationRejected.OnNext(Unit.Default);
                        return false;
                    }
                }
                else
                {
                    // Connection attempt itself failed (server unreachable, timeout, etc.)
                    consecutiveRejections = 0;
                }

                if (cancellationToken.IsCancellationRequested || !_continueToReconnect.CurrentValue)
                    break;

                await WaitBeforeRetry(cancellationToken);
            }

            _logger.LogDebug("{class}[{guid}] {method} Connection loop terminated for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(StartConnectionLoop), Endpoint);
            return false;
        }

        /// <summary>
        /// Attempts to start the connection. Must be called from within a _gate-protected section.
        /// Protected virtual to allow test subclasses to simulate server behavior.
        /// </summary>
        protected virtual async Task<bool> AttemptConnection(CancellationToken cancellationToken)
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
                    // Observe the task to prevent UnobservedTaskException; the exception
                    // (typically OperationCanceledException) is expected and can be ignored.
                    _ = connectionTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.ExecuteSynchronously);
                    return false;
                }

                if (connectionTask.IsCompletedSuccessfully)
                {
                    _logger.LogInformation("{class}[{guid}] {method} Connection established successfully to: {endpoint}",
                        nameof(ConnectionManager), _guid, nameof(AttemptConnection), Endpoint);
                    _transportConnected.OnNext(Unit.Default);
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

        protected virtual async Task WaitBeforeRetry(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            _logger.LogTrace("{class}[{guid}] {method} Waiting 5 seconds before retry for endpoint: {endpoint}",
                nameof(ConnectionManager), _guid, nameof(WaitBeforeRetry), Endpoint);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during delay
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposal during delay
            }
        }
    }
}
