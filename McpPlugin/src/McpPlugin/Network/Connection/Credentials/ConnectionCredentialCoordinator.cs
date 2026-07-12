/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Wires the connection layer's authorization-rejection signal to the account credential provider
    /// (mcp-authorize b7, design 03 Flow B / design 06): when the server rejects the connection repeatedly
    /// (the <c>IConnection.OnAuthorizationRejected</c> 3-strike signal — an invalid or expired JWT), refresh
    /// the token and reconnect. If the refresh fails, the <see cref="PluginCredentialProvider"/> has already
    /// surfaced <see cref="AuthState.SignInRequired"/>, so this coordinator simply stops — the editor UI
    /// prompts the user to sign in again.
    /// <para>
    /// This is a small composition seam the engine plugin creates after building the connection; it owns no
    /// UI and no HTTP. It reconnects via <c>IConnection.Connect</c>, which re-invokes the credential-provider
    /// callback and thus presents the freshly refreshed JWT.
    /// </para>
    /// </summary>
    public sealed class ConnectionCredentialCoordinator : IDisposable
    {
        readonly IConnection _connection;
        readonly PluginCredentialProvider _credentials;
        readonly ILogger? _logger;
        readonly CompositeDisposable _disposables = new CompositeDisposable();
        readonly SemaphoreSlim _handleGate = new SemaphoreSlim(1, 1);
        volatile bool _disposed;

        public ConnectionCredentialCoordinator(IConnection connection, PluginCredentialProvider credentials, ILogger? logger = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _logger = logger;

            // The 3-strike auth-rejection signal is already aggregated by the ConnectionManager (it fires
            // once per rejection burst). Fire-and-forget the async handler; HandleRejectionAsync collapses
            // any overlap with a non-blocking gate.
            _connection.OnAuthorizationRejected
                .Subscribe(_ => OnAuthorizationRejected())
                .AddTo(_disposables);
        }

        // Fire-and-forget bridge from the R3 subscription to the async handler (the discard here is a true
        // discard, not the Unit lambda parameter). HandleRejectionAsync observes its own faults.
        void OnAuthorizationRejected()
            => _ = HandleRejectionAsync();

        /// <summary>
        /// Refresh the credential then reconnect. Returns true when the refresh succeeded and a reconnect
        /// was attempted; false when the refresh failed (sign-in-required is surfaced by the provider) or a
        /// handling pass is already in flight. Public so engines/tests can invoke it directly.
        /// </summary>
        public async Task<bool> HandleRejectionAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return false;

            // Collapse overlapping rejection bursts — only one refresh+reconnect at a time.
            if (!await _handleGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return false;
            try
            {
                _logger?.LogInformation("Authorization rejected — attempting token refresh + reconnect.");

                var refreshed = await _credentials.RefreshAsync(cancellationToken).ConfigureAwait(false);
                if (!refreshed)
                {
                    _logger?.LogWarning("Token refresh failed after authorization rejection; staying disconnected (sign-in required).");
                    return false;
                }

                _logger?.LogInformation("Token refreshed — reconnecting with the new credential.");
                await _connection.Connect(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling authorization rejection: {message}", ex.Message);
                return false;
            }
            finally
            {
                _handleGate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _disposables.Dispose();
            _handleGate.Dispose();
        }
    }
}
