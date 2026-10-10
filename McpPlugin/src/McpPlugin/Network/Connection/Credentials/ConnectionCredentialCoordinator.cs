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
    /// the token and reconnect. Temporary refresh failures retain the credential and retry with backoff.
    /// A definitive credential failure surfaces <see cref="AuthState.SignInRequired"/> and stops recovery
    /// until the user signs in again.
    /// <para>
    /// Stop/resume (oauth-client-error-hygiene 02 §C3): a dead credential must not be re-presented
    /// to the hub every retry period, so <see cref="PluginCredentialProvider.OnSignInRequired"/> —
    /// the dead-family verdict — drives <c>IConnection.Disconnect</c> (which clears the reconnect
    /// intent and settles the loop idle-Disconnected). When authorization returns (the
    /// SignInRequired→SignedIn <b>edge</b> of <see cref="PluginCredentialProvider.State"/>, e.g. a
    /// fresh sign-in or the provider's peer wake-up re-check) the coordinator resumes with one
    /// <c>Connect</c> — only when the consumer's KeepConnected intent was on when the stop
    /// happened, edge-triggered, single-flight, and never while a connect loop is already live
    /// (review finding 12). The connection's <c>_authorizationRejected</c> signal is deliberately
    /// NOT reused for the stop: its subscribers all mean "try to recover" (review P1-26).
    /// </para>
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
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly Func<int, CancellationToken, Task> _retryDelay;
        readonly AsyncLocal<bool> _credentialStopContext = new AsyncLocal<bool>();
        volatile bool _disposed;
        int _pendingRejection;

        /// <summary>Single-flight guard for the OnSignInRequired→Disconnect pass (a flapping state must not stack Disconnects).</summary>
        int _stopInFlight;

        /// <summary>Single-flight guard for the SignedIn-edge→Connect pass (a flapping state must not stack connect loops).</summary>
        int _resumeInFlight;

        /// <summary>
        /// The consumer's KeepConnected intent, captured when the sign-in-required stop happened —
        /// armed by the verdict handler, consumed (once) by the SignedIn edge. Without it a
        /// re-login would resurrect a connection the consumer had deliberately stopped.
        /// </summary>
        volatile bool _resumeOnSignedIn;

        /// <summary>
        /// True while <see cref="HandleRejectionAsync"/> is in flight. The 3-strike signal only
        /// fires from a live connect loop that has ALREADY stopped itself (the cap clears
        /// KeepConnected before signalling), so a dead-family verdict surfaced during this pass
        /// still counts as "the consumer's KeepConnected intent was on".
        /// </summary>
        volatile bool _rejectionHandlingInFlight;

        /// <summary>Previous auth state, for edge-triggered resume (only SignInRequired→SignedIn resumes).</summary>
        AuthState _lastAuthState;

        public ConnectionCredentialCoordinator(IConnection connection, PluginCredentialProvider credentials, ILogger? logger = null)
            : this(connection, credentials, logger, DelayRecoveryAsync)
        {
        }

        internal ConnectionCredentialCoordinator(IConnection connection, PluginCredentialProvider credentials,
            ILogger? logger, Func<int, CancellationToken, Task> retryDelay)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _logger = logger;
            _retryDelay = retryDelay ?? throw new ArgumentNullException(nameof(retryDelay));

            if (_connection is IConnectionRecovery recoverable)
                recoverable.OnDisconnectRequested.Subscribe(_ =>
                {
                    if (!_credentialStopContext.Value)
                        _resumeOnSignedIn = false;
                }).AddTo(_disposables);

            // The 3-strike auth-rejection signal is already aggregated by the ConnectionManager (it fires
            // once per rejection burst). Fire-and-forget the async handler; HandleRejectionAsync collapses
            // any overlap with a non-blocking gate.
            _connection.OnAuthorizationRejected
                .Subscribe(_ => OnAuthorizationRejected())
                .AddTo(_disposables);

            // Stop on the dead-family verdict (02 §C3.1): a dead credential must stop the connect
            // loop instead of hammering the hub with a dead token every retry period.
            _credentials.OnSignInRequired
                .Subscribe(_ => OnSignInRequiredVerdict())
                .AddTo(_disposables);

            // Resume on the SignInRequired→SignedIn EDGE (02 §C3.2). R3 pushes the current value on
            // subscription; seeding _lastAuthState first makes that initial push a non-edge.
            _lastAuthState = _credentials.State.CurrentValue;
            _credentials.State
                .Subscribe(state => OnAuthStateChanged(state))
                .AddTo(_disposables);
        }

        // Fire-and-forget bridge from the R3 subscription to the async handler (the discard here is a true
        // discard, not the Unit lambda parameter). HandleRejectionAsync observes its own faults.
        void OnAuthorizationRejected()
        {
            Interlocked.Exchange(ref _pendingRejection, 1);
            _ = HandleRejectionAsync();
        }

        /// <summary>
        /// Refresh the credential then reconnect. Transient refresh failures retry with backoff while
        /// the credential remains signed in. Stops on a definitive credential failure, sign-out,
        /// cancellation, disposal, or an explicit disconnect. Only one recovery pass runs at a time.
        /// </summary>
        public async Task<bool> HandleRejectionAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return false;

            // Collapse overlapping rejection bursts — only one refresh+reconnect at a time.
            if (!await _handleGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return false;
            Interlocked.Exchange(ref _pendingRejection, 0);
            using var recovery = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            using var disconnectSubscription = (_connection as IConnectionRecovery)?.OnDisconnectRequested
                .Subscribe(_ => recovery.Cancel());
            using var authSubscription = _credentials.State.Subscribe(state =>
            {
                if (state != AuthState.SignedIn)
                    recovery.Cancel();
            });
            try
            {
                _rejectionHandlingInFlight = true;
                _logger?.LogInformation("Authorization rejected — attempting token refresh + reconnect.");

                var retry = 0;
                while (!recovery.IsCancellationRequested)
                {
                    var refreshed = await _credentials.RefreshAsync(recovery.Token).ConfigureAwait(false);
                    recovery.Token.ThrowIfCancellationRequested();
                    if (refreshed)
                    {
                        _logger?.LogInformation("Token refreshed — reconnecting with the new credential.");
                        var connected = _connection is IConnectionRecovery recoverable
                            ? await recoverable.ConnectForRecovery(recovery.Token).ConfigureAwait(false)
                            : await _connection.Connect(recovery.Token).ConfigureAwait(false);
                        recovery.Token.ThrowIfCancellationRequested();
                        if (connected || _connection.KeepConnected.CurrentValue)
                            return true;
                        // Another rejection can exhaust the new cycle before this pass releases
                        // its gate. Continue here rather than dropping that rejection burst.
                        Interlocked.Exchange(ref _pendingRejection, 0);
                    }
                    if (_credentials.State.CurrentValue != AuthState.SignedIn)
                        return false;

                    _logger?.LogWarning("Token refresh temporarily unavailable after authorization rejection; keeping the credential and retrying recovery.");
                    await _retryDelay(retry++, recovery.Token).ConfigureAwait(false);
                }
                return false;
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
                _rejectionHandlingInFlight = false;
                _handleGate.Release();
                if (Interlocked.Exchange(ref _pendingRejection, 0) != 0
                    && !recovery.IsCancellationRequested && !_disposed
                    && _credentials.State.CurrentValue == AuthState.SignedIn)
                    _ = HandleRejectionAsync();
            }
        }

        static Task DelayRecoveryAsync(int retry, CancellationToken cancellationToken)
        {
            // Bounded exponential backoff plus jitter avoids reconnect storms after a deploy.
            var seconds = Math.Min(60, 5 * Math.Pow(2, Math.Min(retry, 4)));
            var jitter = 0.8 + new Random().NextDouble() * 0.4;
            return Task.Delay(TimeSpan.FromSeconds(Math.Min(60, seconds * jitter)), cancellationToken);
        }

        /// <summary>
        /// The dead-family verdict (02 §C3.1): capture the consumer's reconnect intent, then stop
        /// the connect loop via the existing stop primitive (<c>Disconnect</c> clears
        /// <c>_continueToReconnect</c> and settles idle-Disconnected). Single-flight — a flapping
        /// state produces exactly one Disconnect pass; and Disconnect itself is idempotent when
        /// the connection is already down.
        /// </summary>
        void OnSignInRequiredVerdict()
        {
            if (_disposed)
                return;

            // Capture intent BEFORE Disconnect clears it. Either the loop is live right now
            // (KeepConnected), or the verdict surfaced inside a rejection-handling pass — the
            // 3-strike path, whose cap already cleared KeepConnected before signalling but which
            // only ever fires from a loop the consumer had running.
            if (_connection.KeepConnected.CurrentValue || _rejectionHandlingInFlight)
                _resumeOnSignedIn = true;

            if (Interlocked.CompareExchange(ref _stopInFlight, 1, 0) != 0)
                return; // a stop pass is already in flight — flapping must not stack Disconnects
            _ = StopReconnectAsync();
        }

        // Fire-and-forget body of the stop pass; observes its own faults.
        async Task StopReconnectAsync()
        {
            try
            {
                if (_disposed)
                    return;
                _logger?.LogInformation("Sign-in required — stopping reconnection so the dead credential is not re-presented (02 §C3.1). Reconnection resumes on the next sign-in.");
                _credentialStopContext.Value = true;
                await _connection.Disconnect().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping reconnection after sign-in-required: {message}", ex.Message);
            }
            finally
            {
                _credentialStopContext.Value = false;
                Interlocked.Exchange(ref _stopInFlight, 0);
            }
        }

        /// <summary>
        /// Resume on the SignInRequired→SignedIn edge (02 §C3.2): when authorization returns —
        /// fresh sign-in, or the provider's peer wake-up re-check adopting another surface's login
        /// — reconnect, but only when the consumer's KeepConnected intent was on at stop time,
        /// only on the edge (never level-triggered), and never while a connect loop is already
        /// live (review finding 12: a flapping state must not stack loops).
        /// </summary>
        void OnAuthStateChanged(AuthState state)
        {
            var previous = _lastAuthState;
            _lastAuthState = state;

            if (_disposed || previous != AuthState.SignInRequired || state != AuthState.SignedIn)
                return; // edge-triggered: only the SignInRequired→SignedIn transition can resume

            if (!_resumeOnSignedIn)
                return; // the consumer's KeepConnected intent was off — do not resurrect the connection
            _resumeOnSignedIn = false; // consume: one resume per stop; the next verdict re-arms

            if (_connection.KeepConnected.CurrentValue)
                return; // a connect loop is already live — resuming would stack loops (finding 12)

            if (Interlocked.CompareExchange(ref _resumeInFlight, 1, 0) != 0)
                return; // a resume pass is already in flight
            _ = ResumeAsync();
        }

        // Fire-and-forget body of the resume pass; observes its own faults.
        async Task ResumeAsync()
        {
            try
            {
                if (_disposed)
                    return;
                _logger?.LogInformation("Signed in again — resuming the connection (02 §C3.2).");
                await _connection.Connect().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error resuming the connection after sign-in: {message}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _resumeInFlight, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _lifetime.Cancel();
            _disposables.Dispose();
            // An in-flight pass still releases the gate in finally; disposing it here races that
            // release. SemaphoreSlim without AvailableWaitHandle owns no native resource.
            // The cancelled lifetime source likewise stays available to a pass that just acquired
            // the gate. Its linked registrations are disposed by that pass; no timer is allocated.
        }
    }
}
