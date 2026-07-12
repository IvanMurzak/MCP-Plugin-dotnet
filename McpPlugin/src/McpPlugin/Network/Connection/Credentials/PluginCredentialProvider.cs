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
using com.IvanMurzak.McpPlugin.AgentConfig;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The engine-side account credential provider (mcp-authorize b7, design 03 Flow B / design 06). It
    /// owns the machine-store-backed account credential and is the source of the access token the SignalR
    /// connection presents. Three behaviours:
    /// <list type="bullet">
    ///   <item><b>Machine-store auto-adopt (the zero-button rule):</b> the machine credential store is read
    ///   at construction. If a credential exists the provider is <see cref="AuthState.SignedIn"/> with
    ///   <b>zero</b> UI interaction — engines connect signed-in on boot without any editor click.</item>
    ///   <item><b>Proactive refresh:</b> <see cref="GetAccessTokenAsync"/> refreshes the token before its
    ///   <c>exp</c> (within a skew window) so a valid JWT is always presented on (re)connect.</item>
    ///   <item><b>Reactive refresh + sign-in-again:</b> <see cref="RefreshAsync"/> (driven by the connection
    ///   layer's <c>_authorizationRejected</c> 3-strike signal) mints a new token and persists it; a refresh
    ///   failure transitions to <see cref="AuthState.SignInRequired"/> and fires <see cref="OnSignInRequired"/>
    ///   so the engine UI can prompt "Session expired — sign in again".</item>
    /// </list>
    /// The actual token-endpoint HTTP exchange is delegated to an injected <see cref="ITokenRefresher"/>
    /// (each engine/CLI owns it); this class never talks to the network directly and never logs token
    /// material.
    /// </summary>
    public sealed class PluginCredentialProvider : IDisposable
    {
        /// <summary>Default proactive-refresh skew: refresh once the token is within 60s of expiry.</summary>
        public static readonly TimeSpan DefaultRefreshSkew = TimeSpan.FromSeconds(60);

        readonly MachineCredentialStore _store;
        readonly ITokenRefresher? _refresher;
        readonly ILogger? _logger;
        readonly TimeSpan _refreshSkew;
        readonly Func<DateTimeOffset> _clock;

        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly ReactiveProperty<AuthState> _state;
        readonly ReadOnlyReactiveProperty<AuthState> _stateReadOnly;
        readonly Subject<Unit> _signInRequired = new Subject<Unit>();
        MachineCredentials? _current;
        volatile bool _disposed;

        /// <summary>
        /// Construct over a machine credential store. <paramref name="refresher"/> null ⇒ no refresh is
        /// possible (proactive/reactive refresh become no-ops that surface sign-in-required). The optional
        /// <paramref name="clock"/> exists for deterministic expiry tests.
        /// </summary>
        public PluginCredentialProvider(
            MachineCredentialStore store,
            ITokenRefresher? refresher = null,
            ILogger? logger = null,
            TimeSpan? refreshSkew = null,
            Func<DateTimeOffset>? clock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _refresher = refresher;
            _logger = logger;
            _refreshSkew = refreshSkew ?? DefaultRefreshSkew;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);

            // Auto-adopt: read the machine store now. No UI, no device flow. If a credential exists the
            // plugin is already signed in (the zero-button rule — design 06).
            _current = SafeRead();
            _state = new ReactiveProperty<AuthState>(_current?.AccessToken != null ? AuthState.SignedIn : AuthState.SignedOut);
            _stateReadOnly = _state.ToReadOnlyReactiveProperty();
        }

        /// <summary>The current sign-in state (engine UI binds a sign-in chip to this).</summary>
        public ReadOnlyReactiveProperty<AuthState> State => _stateReadOnly;

        /// <summary>Fires when a refresh failed and the user must sign in again (design 06).</summary>
        public Observable<Unit> OnSignInRequired => _signInRequired;

        /// <summary>True when a usable credential is present.</summary>
        public bool IsSignedIn => _state.CurrentValue == AuthState.SignedIn && _current?.AccessToken != null;

        /// <summary>The enrolled server target the current credential was issued for (hosted vs local), if known.</summary>
        public string? ServerTarget => _current?.ServerTarget;

        /// <summary>The account id (<c>sub</c>) the current credential resolves to, if known (diagnostic only).</summary>
        public string? Subject => _current?.Subject;

        /// <summary>
        /// The <c>Func&lt;Task&lt;string?&gt;&gt;</c> to assign to
        /// <see cref="ConnectionConfig.CredentialProvider"/> / SignalR's <c>AccessTokenProvider</c>. It
        /// returns the current (proactively-refreshed) access token, or null when signed out.
        /// </summary>
        public Func<Task<string?>> AsAccessTokenProvider()
            => () => GetAccessTokenAsync(CancellationToken.None);

        /// <summary>
        /// Returns the current access token, proactively refreshing first when it is within the skew window
        /// of expiry. Returns null when signed out (anonymous / <c>none</c> mode). Never throws for a normal
        /// refresh failure — it returns the existing token and surfaces sign-in-required, letting the
        /// server's rejection path drive recovery.
        /// </summary>
        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_current?.AccessToken == null)
                    return null;

                if (ShouldRefresh(_current))
                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);

                return _current?.AccessToken;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Refresh the access token now (driven by the connection layer's <c>_authorizationRejected</c>
        /// signal). Returns true when a fresh token was persisted; false when refresh failed or is
        /// impossible (in which case <see cref="AuthState.SignInRequired"/> has been surfaced).
        /// </summary>
        public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return false;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Adopt a freshly obtained credential (e.g. after an in-editor device-flow sign-in). Persists it to
        /// the machine store and transitions to <see cref="AuthState.SignedIn"/>.
        /// </summary>
        public void Adopt(MachineCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            _gate.Wait();
            try
            {
                _store.Write(credentials);
                _current = credentials;
                _state.Value = credentials.AccessToken != null ? AuthState.SignedIn : AuthState.SignedOut;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Sign out: delete the stored credential and reset to <see cref="AuthState.SignedOut"/>.</summary>
        public void SignOut()
        {
            _gate.Wait();
            try
            {
                try { _store.Delete(); }
                catch (Exception ex) { _logger?.LogWarning("Deleting stored credential failed: {message}", ex.Message); }
                _current = null;
                _state.Value = AuthState.SignedOut;
            }
            finally
            {
                _gate.Release();
            }
        }

        // MUST be called with _gate held.
        async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
        {
            var current = _current;
            if (current?.RefreshToken == null || _refresher == null)
            {
                SurfaceSignInRequired("no refresh token or refresher configured");
                return false;
            }

            TokenRefreshResult result;
            try
            {
                result = await _refresher.RefreshAsync(current.RefreshToken, current.ServerTarget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Token refresh threw: {message}", ex.Message); // never log token material
                SurfaceSignInRequired("refresh error");
                return false;
            }

            if (result == null || !result.Succeeded || string.IsNullOrEmpty(result.AccessToken))
            {
                SurfaceSignInRequired(result?.FailureReason);
                return false;
            }

            var refreshToken = string.IsNullOrEmpty(result.RefreshToken) ? current.RefreshToken! : result.RefreshToken!;

            MachineCredentials rotated;
            try
            {
                // Rotate() preserves the stored identity fields (ServerTarget / Subject) and persists.
                rotated = _store.Rotate(result.AccessToken!, refreshToken, result.ExpiresAt);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Persisting refreshed credential failed: {message}", ex.Message);
                // Keep the refreshed token in memory even if the disk write failed — the connection can proceed.
                rotated = new MachineCredentials
                {
                    AccessToken = result.AccessToken,
                    RefreshToken = refreshToken,
                    ExpiresAt = result.ExpiresAt,
                    ServerTarget = current.ServerTarget,
                    Subject = current.Subject
                };
            }

            _current = rotated;
            _state.Value = AuthState.SignedIn;
            return true;
        }

        bool ShouldRefresh(MachineCredentials credentials)
        {
            if (_refresher == null)
                return false;
            if (credentials.ExpiresAt == null)
                return false; // unknown expiry — recover reactively on server rejection instead
            return credentials.ExpiresAt.Value - _clock() <= _refreshSkew;
        }

        void SurfaceSignInRequired(string? reason)
        {
            _logger?.LogWarning("Account credential refresh failed ({reason}); sign-in required.", reason ?? "unknown");
            if (_disposed)
                return;
            _state.Value = AuthState.SignInRequired;
            _signInRequired.OnNext(Unit.Default);
        }

        MachineCredentials? SafeRead()
        {
            try
            {
                return _store.Read();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Reading the machine credential store failed: {message}", ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _signInRequired.Dispose();
            _stateReadOnly.Dispose();
            _state.Dispose();
            _gate.Dispose();
        }
    }
}
