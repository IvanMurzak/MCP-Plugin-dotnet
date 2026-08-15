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
    /// The engine-side account credential provider (mcp-authorize b7, unified-machine-auth b3). It
    /// owns the machine-store-backed <b>plugin-plane</b> credential (v2 <c>families.plugin</c>,
    /// falling back to a v1-adopted <c>families.legacy</c>) and is the source of the access token
    /// the SignalR connection presents. Behaviours:
    /// <list type="bullet">
    ///   <item><b>Machine-store auto-adopt (the zero-button rule):</b> the machine credential store is read
    ///   at construction. If a plugin-plane credential exists the provider is <see cref="AuthState.SignedIn"/>
    ///   with <b>zero</b> UI interaction — engines connect signed-in on boot without any editor click.</item>
    ///   <item><b>Lock-integrated double-checked refresh (04 §2/§3, design 03 F3):</b> both the
    ///   proactive path (<see cref="GetAccessTokenAsync"/>, 60 s expiry skew) and the reactive path
    ///   (<see cref="RefreshAsync"/>, driven by the connection layer's 401 3-strike signal) run
    ///   under the cross-process <see cref="MachineCredentialLock"/> and <b>re-read the store
    ///   first</b>: a peer that already refreshed is adopted without a network call. A lock budget
    ///   exhausted, or a store that reads back <see cref="MachineCredentialStoreStatus.Unreadable"/>
    ///   mid-refresh, fails the attempt as <b>busy</b> (transient — no sign-in-required, no
    ///   overwrite, never lock-free).</item>
    ///   <item><b>Family rules (04 §3):</b> the refresher is handed the family's stored
    ///   <c>clientId</c> (null for a legacy family — the refresher presents its component default,
    ///   §3.7); a rotation response without a new refresh token keeps the previous one; a
    ///   successful legacy-family refresh rewrites the document as v2 <c>families.legacy</c>
    ///   (+ v1 mirror) via the normal write contract. Family-aware writes go through
    ///   read-modify-<see cref="MachineCredentialStore.Write"/> under the lock — never through
    ///   <see cref="MachineCredentialStore.Rotate"/> (b1 review A3: its plugin/legacy fallback is
    ///   not family-explicit and its unreadable-refusal throw is the wrong shape mid-lock).</item>
    ///   <item><b>Failure-state split (04 §3.5, 03 F3.5/F9):</b> <see cref="AuthState.SignInRequired"/>
    ///   is the DEAD-FAMILY verdict only — <c>invalid_grant</c> followed by a post-failure re-read
    ///   that shows nobody else rotated the family. Then ONE structured telemetry event is emitted
    ///   (per family death, not per retry), other families are never deleted, and the provider
    ///   never loops — one network attempt per call, rate-bounded across calls by the refresher
    ///   (04 §3.6). Every OTHER failure — AS unreachable, 5xx, timeout, a thrown refresher — is
    ///   <b>transient</b>: the provider stays <see cref="AuthState.SignedIn"/> on its current
    ///   credential and retries later (F9: offline self-heals silently, no sign-in prompt).</item>
    /// </list>
    /// The actual token-endpoint HTTP exchange is delegated to an injected <see cref="ITokenRefresher"/>
    /// (the shared <see cref="HttpTokenRefresher"/> in production); this class never talks to the
    /// network directly and never logs token material.
    /// </summary>
    public sealed class PluginCredentialProvider : IDisposable
    {
        /// <summary>Default proactive-refresh skew: refresh once the token is within 60s of expiry.</summary>
        public static readonly TimeSpan DefaultRefreshSkew = TimeSpan.FromSeconds(60);

        readonly MachineCredentialStore _store;
        readonly MachineCredentialLock _lock;
        readonly ITokenRefresher? _refresher;
        readonly ILogger? _logger;
        readonly TimeSpan _refreshSkew;
        readonly Func<DateTimeOffset> _clock;

        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly ReactiveProperty<AuthState> _state;
        readonly ReadOnlyReactiveProperty<AuthState> _stateReadOnly;
        readonly Subject<Unit> _signInRequired = new Subject<Unit>();
        MachineCredentials? _current;
        bool _familyDeadTelemetryEmitted;
        volatile bool _disposed;

        /// <summary>
        /// Construct over a machine credential store. <paramref name="refresher"/> null ⇒ no refresh is
        /// possible (proactive/reactive refresh become no-ops that surface sign-in-required). The optional
        /// <paramref name="clock"/> exists for deterministic expiry tests.
        /// <paramref name="credentialLock"/> — the cross-process lock guarding refresh writes
        /// (04 §2); null creates the default lock rooted at the store's own directory, so
        /// production callers get cross-process safety without wiring anything.
        /// </summary>
        public PluginCredentialProvider(
            MachineCredentialStore store,
            ITokenRefresher? refresher = null,
            ILogger? logger = null,
            TimeSpan? refreshSkew = null,
            Func<DateTimeOffset>? clock = null,
            MachineCredentialLock? credentialLock = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _lock = credentialLock ?? new MachineCredentialLock(store.BaseDirectory);
            _refresher = refresher;
            _logger = logger;
            _refreshSkew = refreshSkew ?? DefaultRefreshSkew;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);

            // Auto-adopt: read the machine store now. No UI, no device flow. If a plugin-plane
            // credential exists the plugin is already signed in (the zero-button rule — design 06).
            // An unreadable store is NOT signed-out (04 §1): boot into SignInRequired so the UI
            // shows "sign in required (store unreadable)" instead of pretending the machine is
            // clean — and never touch the file.
            var read = SafeRead();
            _current = read.Credentials;
            var initialState = PluginPlaneFamily(_current)?.AccessToken != null
                ? AuthState.SignedIn
                : read.Status == MachineCredentialStoreStatus.Unreadable
                    ? AuthState.SignInRequired
                    : AuthState.SignedOut;
            _state = new ReactiveProperty<AuthState>(initialState);
            _stateReadOnly = _state.ToReadOnlyReactiveProperty();
        }

        /// <summary>The current sign-in state (engine UI binds a sign-in chip to this).</summary>
        public ReadOnlyReactiveProperty<AuthState> State => _stateReadOnly;

        /// <summary>Fires when a refresh failed and the user must sign in again (design 06).</summary>
        public Observable<Unit> OnSignInRequired => _signInRequired;

        /// <summary>True when a usable plugin-plane credential is present.</summary>
        public bool IsSignedIn => _state.CurrentValue == AuthState.SignedIn && PluginPlaneFamily(_current)?.AccessToken != null;

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
                var family = PluginPlaneFamily(_current);
                if (family?.AccessToken == null)
                    return null;

                if (ShouldRefresh(family))
                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);

                return PluginPlaneFamily(_current)?.AccessToken;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Refresh the access token now (driven by the connection layer's <c>_authorizationRejected</c>
        /// signal). Returns true when a fresh token was persisted (or adopted from a peer's refresh);
        /// false when this attempt did not produce one. A false return surfaces
        /// <see cref="AuthState.SignInRequired"/> ONLY for a dead family (<c>invalid_grant</c>
        /// confirmed by the post-failure re-read) or when refresh is impossible (no refresh token /
        /// no refresher); transient failures and a busy machine-wide lock leave the state untouched
        /// (retry-later semantics — review B2).
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
        /// <para>Behavioral seam (b1 review A1, pinned by test): <see cref="MachineCredentialStore.Write"/>
        /// normalizes its ARGUMENT in place (v1 adoption → <c>families.legacy</c>, version upgrade,
        /// mirror stamp), and this method deliberately keeps that same mutated instance as the
        /// provider's current credential — so a caller passing top-level-only token material ends up
        /// with a family-shaped <c>_current</c> the refresh path can operate on.</para>
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
                _familyDeadTelemetryEmitted = false;
                _state.Value = PluginPlaneFamily(credentials)?.AccessToken != null ? AuthState.SignedIn : AuthState.SignedOut;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Sign out this provider: delete the stored credential (via the 04 §2 lock-protocol delete
        /// path) and reset to <see cref="AuthState.SignedOut"/>. When the lock is busy the file is
        /// left for the holder (the local state still signs out); machine-wide sign-out with
        /// server-side revocation is <see cref="MachineWideSignOut"/> (F6).
        /// </summary>
        public void SignOut()
        {
            _gate.Wait();
            try
            {
                try
                {
                    if (!_lock.TryDeleteStore(_store))
                        _logger?.LogWarning("Credential store delete skipped: the machine-wide credential lock is busy.");
                }
                catch (Exception ex) { _logger?.LogWarning("Deleting stored credential failed: {message}", ex.Message); }
                _current = null;
                _familyDeadTelemetryEmitted = false;
                _state.Value = AuthState.SignedOut;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// The plugin-plane family of a credential document (04 §1): <c>families.plugin</c>,
        /// falling back to <c>families.legacy</c> (a v1-adopted document's only plugin-plane
        /// credential). Defensively adopts top-level v1 token material first (idempotent), so even
        /// a document that skipped the store's read path is family-shaped.
        /// </summary>
        static MachineCredentialFamily? PluginPlaneFamily(MachineCredentials? credentials)
        {
            if (credentials == null)
                return null;
            credentials.AdoptLegacyTokens();
            return credentials.Families?.Plugin ?? credentials.Families?.Legacy;
        }

        // MUST be called with _gate held. One network attempt maximum — never loops (04 §3.5).
        async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
        {
            var current = _current;
            var family = PluginPlaneFamily(current);
            if (current == null || family?.RefreshToken == null || _refresher == null)
            {
                SurfaceSignInRequired("no refresh token or refresher configured");
                return false;
            }

            // Cross-process lock (04 §2). TryAcquire blocks (jittered backoff, ≤75 s budget), so it
            // runs on the pool — never on an engine main thread. Budget exhausted ⇒ busy: fail THIS
            // attempt, no state change, never proceed lock-free (D9 REVISED).
            MachineCredentialLockHandle? lockHandle;
            try
            {
                lockHandle = await Task.Run(() => _lock.TryAcquire(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            if (lockHandle == null)
            {
                _logger?.LogInformation("Token refresh skipped: the machine-wide credential lock is busy (a peer is refreshing). Will retry next cycle.");
                return false;
            }

            using (lockHandle)
            {
                // Double-checked (03 F3.2): RE-READ the store under the lock before any network.
                var reread = _store.TryRead();
                if (reread.Status == MachineCredentialStoreStatus.Unreadable)
                {
                    // Unreadable ≠ dead (b1 review A3): transient contention (a concurrent atomic
                    // replace, AV holding the file) or a real DPAPI loss — either way: busy/retry,
                    // never signed-out from here, and NEVER overwrite the file.
                    _logger?.LogWarning("Token refresh skipped: credential store unreadable right now ({error}); treating as busy.", reread.Error);
                    return false;
                }
                if (reread.Status == MachineCredentialStoreStatus.Missing || reread.Credentials == null)
                {
                    // A peer signed the machine out (F6.3) — adopt the signed-out state, quietly.
                    _current = null;
                    _state.Value = AuthState.SignedOut;
                    return false;
                }

                var latest = reread.Credentials;
                var latestFamily = PluginPlaneFamily(latest);
                if (latestFamily?.AccessToken != null && latestFamily.AccessToken != family.AccessToken)
                {
                    // A peer already refreshed — adopt its tokens, no network call (03 F3.2).
                    AdoptInMemory(latest);
                    return true;
                }
                if (latestFamily?.RefreshToken == null)
                {
                    SurfaceSignInRequired("stored credential has no refresh token");
                    return false;
                }

                // Still stale — one network attempt, presenting the family's STORED clientId
                // (04 §3.2; null for legacy ⇒ the refresher's component default, §3.7).
                var request = new TokenRefreshRequest(
                    latestFamily.RefreshToken!,
                    latest.ServerTarget ?? current.ServerTarget,
                    latestFamily.ClientId);

                TokenRefreshResult result;
                try
                {
                    result = await _refresher.RefreshAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A thrown refresh (network down, DNS, TLS) is TRANSIENT (review B2): keep the
                    // current credential and state, retry later — offline self-heals silently (F9).
                    _logger?.LogWarning("Token refresh threw ({message}); staying signed in, will retry.", ex.Message); // never log token material
                    return false;
                }

                if (result == null || !result.Succeeded || string.IsNullOrEmpty(result.AccessToken))
                    return HandleRefreshFailure(result, latestFamily);

                // Success: family-aware write (NOT Store.Rotate — b1 review A3). Rotation response
                // without a new refresh token keeps the previous one (04 §3.4). For a v1-adopted
                // document this mutates families.legacy and Write() upgrades it to a v2 document
                // (+ v1 mirror) — the §3.7 rewrite — while never touching other families.
                var refreshToken = string.IsNullOrEmpty(result.RefreshToken) ? latestFamily.RefreshToken! : result.RefreshToken!;
                latestFamily.AccessToken = result.AccessToken;
                latestFamily.RefreshToken = refreshToken;
                latestFamily.ExpiresAt = result.ExpiresAt;

                try
                {
                    // Verify the lock is still ours immediately before the write (b2 review A1) —
                    // a (clock-skew) stale takeover mid-call must not be answered with a write.
                    if (lockHandle.IsStillOwned())
                        _store.Write(latest);
                    else
                        _logger?.LogWarning("Credential lock was taken over mid-refresh; keeping the refreshed token in memory and skipping the disk write.");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Persisting refreshed credential failed: {message}", ex.Message);
                    // Keep the refreshed token in memory even if the disk write failed — the connection can proceed.
                }

                AdoptInMemory(latest);
                return true;
            }
        }

        // MUST be called with _gate held, INSIDE the lock's using scope.
        bool HandleRefreshFailure(TokenRefreshResult? result, MachineCredentialFamily attemptedFamily)
        {
            if (result?.FailureKind == TokenRefreshFailureKind.InvalidGrant)
            {
                // 03 F3.5: re-read once more — a racer may have won legitimately (its rotation
                // revoked our just-presented token, which the AS reports as invalid_grant).
                var post = _store.TryRead();
                if (post.Status == MachineCredentialStoreStatus.Unreadable)
                {
                    _logger?.LogWarning("Post-failure store re-read is unreadable ({error}); treating as busy.", post.Error);
                    return false; // busy/retry, never a dead-family verdict off an unreadable read (b1 A3)
                }
                if (post.Status == MachineCredentialStoreStatus.Ok && post.Credentials != null)
                {
                    var postFamily = PluginPlaneFamily(post.Credentials);
                    if (postFamily?.RefreshToken != null && postFamily.RefreshToken != attemptedFamily.RefreshToken)
                    {
                        AdoptInMemory(post.Credentials); // the racer's rotation is the live credential
                        return true;
                    }
                }

                // Family dead (04 §3.5): ONE structured telemetry event; never delete other
                // families (the store is not touched at all); never loop.
                if (!_familyDeadTelemetryEmitted)
                {
                    _familyDeadTelemetryEmitted = true;
                    _logger?.LogWarning(
                        "Credential family dead: refresh returned invalid_grant and no peer rotated it. family={family} clientIdKnown={clientIdKnown} reason={reason}",
                        attemptedFamily.ClientId != null ? "plugin" : "legacy",
                        attemptedFamily.ClientId != null,
                        result?.FailureReason ?? "invalid_grant");
                }
                SurfaceSignInRequired(result?.FailureReason ?? "invalid_grant");
                return false;
            }

            // TRANSIENT failure (review B2): SignInRequired is the dead-family verdict ONLY
            // (03 F3.5); everything else — AS unreachable, 5xx, timeout, other OAuth errors —
            // keeps the current credential and state and retries later (F9: offline self-heals
            // silently; rate discipline in the refresher bounds the retries).
            _logger?.LogWarning("Token refresh failed transiently ({reason}); staying signed in, will retry.",
                result?.FailureReason ?? "unknown");
            return false;
        }

        // MUST be called with _gate held.
        void AdoptInMemory(MachineCredentials credentials)
        {
            _current = credentials;
            _familyDeadTelemetryEmitted = false;
            _state.Value = AuthState.SignedIn;
        }

        bool ShouldRefresh(MachineCredentialFamily family)
        {
            if (_refresher == null)
                return false;
            if (family.ExpiresAt == null)
                return false; // unknown expiry — recover reactively on server rejection instead
            return family.ExpiresAt.Value - _clock() <= _refreshSkew;
        }

        void SurfaceSignInRequired(string? reason)
        {
            _logger?.LogWarning("Account credential refresh failed ({reason}); sign-in required.", reason ?? "unknown");
            if (_disposed)
                return;
            _state.Value = AuthState.SignInRequired;
            _signInRequired.OnNext(Unit.Default);
        }

        MachineCredentialReadResult SafeRead()
        {
            try
            {
                return _store.TryRead();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Reading the machine credential store failed: {message}", ex.Message);
                return MachineCredentialReadResult.Unreadable(ex.Message);
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
