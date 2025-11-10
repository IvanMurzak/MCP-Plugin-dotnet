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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public partial class ConnectionManager : IConnectionManager, IAsyncDisposable
    {
        public async Task Disconnect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{class}[{guid}] {method} called but already disposed, ignored.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
                return; // already disposed
            }

            _logger.LogDebug("{class}[{guid}] {method} called.",
                nameof(ConnectionManager), _guid, nameof(Disconnect));

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{class}[{guid}] {method} canceled before it gets started.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
                return;
            }

            // Cancel the internal token to stop any ongoing connection attempts
            CancelInternalToken(dispose: false);
            _continueToReconnect.Value = false;

            try
            {
                _logger.LogDebug("{class}[{guid}] {method} acquiring gate.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));

                await _gate.WaitAsync(cancellationToken);

                _logger.LogDebug("{class}[{guid}] {method} acquired gate.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
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
                _logger.LogDebug("{class}[{guid}] {method} releasing gate.",
                    nameof(ConnectionManager), _guid, nameof(Disconnect));
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

            // Cancel the internal token to stop any ongoing connection attempts
            CancelInternalToken(dispose: false);
            _continueToReconnect.Value = false;

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

            // Clear the ongoing connection task to prevent new Connect calls from waiting for it
            await _ongoingConnectionGate.WaitAsync(cancellationToken);
            _ongoingConnectionTask = null;
            _ongoingConnectionGate.Release();

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
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("{class}[{guid}] {method} HubConnection stop was canceled: {message}",
                    nameof(ConnectionManager), _guid, nameof(DisconnectGracefulAsync), ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError("{class}[{guid}] {method} Invalid operation while stopping HubConnection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(DisconnectGracefulAsync), ex.Message, ex.StackTrace);
            }
            catch (Exception ex)
            {
                _logger.LogCritical("{class}[{guid}] {method} Unexpected error while stopping HubConnection: {message}\n{stackTrace}",
                    nameof(ConnectionManager), _guid, nameof(DisconnectGracefulAsync), ex.Message, ex.StackTrace);
                throw;
            }
        }
    }
}
