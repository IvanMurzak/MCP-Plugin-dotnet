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
    ///   is the DEAD-FAMILY verdict only — a terminal refresh error (<c>invalid_grant</c>, or
    ///   <c>invalid_target</c> as a "server configuration error") followed by a post-failure
    ///   re-read that shows nobody else rotated the family. Then ONE structured telemetry event is
    ///   emitted (per family death, not per retry), other families are never deleted, and the
    ///   provider <b>never loops</b> (oauth-client-error-hygiene 02 §C1): the dead refresh token is
    ///   memoized in-process and never re-presented to the server — re-armed only when the machine
    ///   store holds a DIFFERENT token, i.e. a fresh login by any surface (App, CLI, another
    ///   editor). While sign-in is required, a low-frequency store re-check (02 §C3.3,
    ///   <see cref="DefaultDeadFamilyRecheckPeriod"/>) adopts a peer surface's fresh login so a
    ///   stopped editor wakes up without any caller driving the token path. Every OTHER failure —
    ///   AS unreachable, 5xx, timeout, a thrown refresher — is <b>transient</b>: the provider stays
    ///   <see cref="AuthState.SignedIn"/> on its current credential and retries later (F9: offline
    ///   self-heals silently, no sign-in prompt).</item>
    /// </list>
    /// The actual token-endpoint HTTP exchange is delegated to an injected <see cref="ITokenRefresher"/>
    /// (the shared <see cref="HttpTokenRefresher"/> in production); this class never talks to the
    /// network directly and never logs token material.
    /// </summary>
    public sealed class PluginCredentialProvider : IDisposable
    {
        /// <summary>Default proactive-refresh skew: refresh once the token is within 60s of expiry.</summary>
        public static readonly TimeSpan DefaultRefreshSkew = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Default period of the low-frequency machine-store re-check that runs while the provider
        /// is parked in <see cref="AuthState.SignInRequired"/> after a dead-family verdict
        /// (oauth-client-error-hygiene 02 §C3.3): a peer surface's fresh login is adopted within
        /// one period without any caller driving the token path.
        /// </summary>
        public static readonly TimeSpan DefaultDeadFamilyRecheckPeriod = TimeSpan.FromSeconds(60);

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

        /// <summary>
        /// The dead-family memo (04 §3.5 "never loop", 02 §C1): the refresh token the server
        /// declared dead plus the surfaced reason. ONE nullable field — the provider manages a
        /// single plane (<c>families.plugin</c> + legacy fallback), so no map, no persistence.
        /// Guarded by <see cref="_gate"/>. Armed only by a confirmed dead-family verdict; cleared
        /// (re-armed) when the freshly-read store token differs, on any in-memory adoption of a
        /// live credential, and on sign-out.
        /// </summary>
        (string RefreshToken, string Reason)? _deadFamilyMemo;

        /// <summary>The live dead-family store re-check loop (02 §C3.3), null when not running. Guarded by <see cref="_gate"/> (see <see cref="StopDeadFamilyRecheck"/> for the Dispose exception).</summary>
        CancellationTokenSource? _deadFamilyRecheckCts;
        readonly TimeSpan _deadFamilyRecheckPeriod;

        /// <summary>Reason behind the current SignInRequired state (reference writes are atomic; written under <see cref="_gate"/>, read lock-free by UI).</summary>
        volatile string? _signInRequiredReason;
        volatile bool _disposed;

        /// <summary>
        /// Construct over a machine credential store. <paramref name="refresher"/> null ⇒ no refresh is
        /// possible (proactive/reactive refresh become no-ops that surface sign-in-required). The optional
        /// <paramref name="clock"/> exists for deterministic expiry tests.
        /// <paramref name="credentialLock"/> — the cross-process lock guarding refresh writes
        /// (04 §2); null creates the default lock rooted at the store's own directory, so
        /// production callers get cross-process safety without wiring anything.
        /// <paramref name="deadFamilyRecheckPeriod"/> — period of the low-frequency store re-check
        /// while parked in SignInRequired after a dead-family verdict (02 §C3.3); null uses
        /// <see cref="DefaultDeadFamilyRecheckPeriod"/> (test seam — the injected clock cannot
        /// drive a real delay).
        /// </summary>
        public PluginCredentialProvider(
            MachineCredentialStore store,
            ITokenRefresher? refresher = null,
            ILogger? logger = null,
            TimeSpan? refreshSkew = null,
            Func<DateTimeOffset>? clock = null,
            MachineCredentialLock? credentialLock = null,
            TimeSpan? deadFamilyRecheckPeriod = null)
        {
            _deadFamilyRecheckPeriod = deadFamilyRecheckPeriod ?? DefaultDeadFamilyRecheckPeriod;
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

        /// <summary>
        /// The reason behind the current <see cref="AuthState.SignInRequired"/> state; null in any
        /// other state, and null for a boot-time unreadable-store SignInRequired that predates any
        /// refresh verdict. For an <c>invalid_target</c> dead-family verdict the reason carries the
        /// <c>"server configuration error"</c> class prefix (oauth-client-error-hygiene 02 §C2) so
        /// engine UIs can render the D4 step-3 status without parsing OAuth error codes; the raw
        /// OAuth <c>error</c>/<c>error_description</c> is preserved after the prefix. Never
        /// contains token material.
        /// </summary>
        public string? SignInRequiredReason => _signInRequiredReason;

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
        /// <see cref="AuthState.SignInRequired"/> ONLY for a dead family (a terminal
        /// <c>invalid_grant</c>/<c>invalid_target</c> confirmed by the post-failure re-read) or when
        /// refresh is impossible (no refresh token / no refresher); transient failures and a busy
        /// machine-wide lock leave the state untouched (retry-later semantics — review B2). Once a
        /// family is memoized dead (04 §3.5 / 02 §C1) further calls return false WITHOUT a network
        /// attempt until the machine store holds a different token.
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
                _deadFamilyMemo = null; // a fresh sign-in re-arms refresh (02 §C1)
                _signInRequiredReason = null;
                StopDeadFamilyRecheck();
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
                _deadFamilyMemo = null;
                _signInRequiredReason = null;
                StopDeadFamilyRecheck();
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
                    _deadFamilyMemo = null; // no credential left to memoize against
                    _signInRequiredReason = null;
                    StopDeadFamilyRecheck();
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

                // Dead-family memo (04 §3.5 "never loop", 02 §C1): a refresh family the server
                // declared dead is NEVER re-presented by this process. Compared against the
                // freshly-READ store token — not _current — so a fresh login by ANY surface
                // (App, CLI, another editor) re-arms this process naturally (the .NET adaptation
                // of TS credential-provider Rule 5, keyed on the in-lock re-read).
                if (_deadFamilyMemo != null)
                {
                    if (string.Equals(_deadFamilyMemo.Value.RefreshToken, latestFamily.RefreshToken, StringComparison.Ordinal))
                    {
                        // The store still holds the same dead token: no network attempt, no repeat
                        // telemetry (the ONE structured event fired at the verdict), no state
                        // churn. Debug on purpose — silence IS the fix for the 1,440/day loop.
                        _logger?.LogDebug("Token refresh suppressed: this credential family was declared dead ({reason}); waiting for a new sign-in.", _deadFamilyMemo.Value.Reason);
                        return false;
                    }
                    _deadFamilyMemo = null; // the store holds a DIFFERENT token — re-arm and proceed
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

        // MUST be called with _gate held, INSIDE the lock's using scope. attemptedFamily's
        // RefreshToken is non-null — the caller verified it before the network attempt.
        bool HandleRefreshFailure(TokenRefreshResult? result, MachineCredentialFamily attemptedFamily)
        {
            var kind = result?.FailureKind ?? TokenRefreshFailureKind.Transient;
            if (kind == TokenRefreshFailureKind.InvalidGrant || kind == TokenRefreshFailureKind.InvalidTarget)
            {
                // 03 F3.5: re-read once more — a racer may have won legitimately (its rotation
                // revoked our just-presented token, which the AS reports as invalid_grant). This
                // stays FIRST: a racer's rotation still wins over the dead-family verdict.
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
                // families (the store is not touched at all); never loop (memo below).
                // invalid_target is the "server configuration error" class (02 §C2) — a server-
                // side audience/resource mismatch that re-auth does not reliably cure — so its
                // surfaced reason carries a distinct class prefix; the raw OAuth error string
                // stays verbatim after it for telemetry.
                var errorCode = kind == TokenRefreshFailureKind.InvalidTarget ? "invalid_target" : "invalid_grant";
                var reason = kind == TokenRefreshFailureKind.InvalidTarget
                    ? "server configuration error: " + (result?.FailureReason ?? errorCode)
                    : result?.FailureReason ?? errorCode;
                if (!_familyDeadTelemetryEmitted)
                {
                    _familyDeadTelemetryEmitted = true;
                    _logger?.LogWarning(
                        "Credential family dead: refresh returned {errorCode} and no peer rotated it. family={family} clientIdKnown={clientIdKnown} reason={reason}",
                        errorCode,
                        attemptedFamily.ClientId != null ? "plugin" : "legacy",
                        attemptedFamily.ClientId != null,
                        reason);
                }

                // Never loop (04 §3.5, 02 §C1): memoize the dead token — RefreshCoreAsync will not
                // re-present it until the machine store holds a different one. In-process only;
                // the store itself is never written or deleted on any failure path
                // (unified-machine-auth 03-auth-flows.md:52).
                _deadFamilyMemo = (attemptedFamily.RefreshToken!, reason);

                // Peer wake-up (02 §C3.3): while parked in SignInRequired, a low-frequency store
                // re-check adopts a peer surface's fresh login — the machine-wide "one login
                // anywhere authorizes everywhere" model for stopped editors.
                StartDeadFamilyRecheck();

                SurfaceSignInRequired(reason);
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
            _deadFamilyMemo = null; // a live credential re-arms refresh (02 §C1)
            _signInRequiredReason = null;
            StopDeadFamilyRecheck();
            _state.Value = AuthState.SignedIn;
        }

        // MUST be called with _gate held. Starts the low-frequency dead-family store re-check
        // (02 §C3.3) unless one is already running. Idempotent per verdict; self-terminating —
        // the loop stops itself on any state other than SignInRequired and on disposal.
        void StartDeadFamilyRecheck()
        {
            if (_deadFamilyRecheckCts != null || _disposed)
                return;
            var cts = new CancellationTokenSource();
            _deadFamilyRecheckCts = cts;
            _ = RunDeadFamilyRecheckAsync(cts);
        }

        // MUST be called with _gate held — except from Dispose, which runs after _disposed is set
        // (the loop checks _disposed under the gate before acting, so a racing tick self-stops).
        void StopDeadFamilyRecheck()
        {
            var cts = _deadFamilyRecheckCts;
            _deadFamilyRecheckCts = null;
            if (cts == null)
                return;
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException) { }
        }

        // The peer wake-up loop (02 §C3.3): while the provider is parked in SignInRequired after a
        // dead-family verdict, re-check the machine store once per period; when it holds token
        // material that differs from the memoized dead family, run the normal lock-integrated
        // refresh path (which adopts a peer's fresh credential without a network call, or — memo
        // re-armed by the differing token — performs one refresh) and reach SignedIn. No new
        // threads: an async Task.Delay loop in repo style, cancelled via <see cref="_deadFamilyRecheckCts"/>.
        async Task RunDeadFamilyRecheckAsync(CancellationTokenSource owner)
        {
            var cancellationToken = owner.Token;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_deadFamilyRecheckPeriod, cancellationToken).ConfigureAwait(false);

                    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    bool stop;
                    try
                    {
                        stop = await DeadFamilyRecheckTickAsync(owner, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                    if (stop)
                        return;
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { } // disposal races the loop's own primitives — a stop, not an error
            catch (Exception ex)
            {
                _logger?.LogWarning("Dead-family store re-check stopped unexpectedly: {message}", ex.Message); // never log token material
            }
        }

        // MUST be called with _gate held (one tick of the wake-up loop). True ⇒ the loop stops.
        async Task<bool> DeadFamilyRecheckTickAsync(CancellationTokenSource owner, CancellationToken cancellationToken)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
                return true;
            if (_state.CurrentValue != AuthState.SignInRequired)
            {
                // Recovered (or signed out) through another path — self-terminate.
                if (ReferenceEquals(_deadFamilyRecheckCts, owner))
                    StopDeadFamilyRecheck();
                return true;
            }

            // Cheap lock-free READ first: the full cross-process-lock protocol runs only when the
            // store actually holds something new (a peer surface's login), so ~all ticks of a
            // parked editor cost one file read and zero lock traffic.
            var read = SafeRead();
            var storeFamily = PluginPlaneFamily(read.Credentials);
            var currentFamily = PluginPlaneFamily(_current);
            var deadToken = _deadFamilyMemo?.RefreshToken ?? currentFamily?.RefreshToken;
            var storeHoldsSomethingNew = storeFamily != null
                && ((storeFamily.RefreshToken != null && deadToken != null
                        && !string.Equals(storeFamily.RefreshToken, deadToken, StringComparison.Ordinal))
                    || (storeFamily.AccessToken != null && currentFamily?.AccessToken != null
                        && !string.Equals(storeFamily.AccessToken, currentFamily.AccessToken, StringComparison.Ordinal)));
            if (!storeHoldsSomethingNew)
                return false; // nothing new — stay parked, re-check next period

            // Re-arm + refresh through the normal path (re-reads under the cross-process lock;
            // adopts, or refreshes with the store's token). Transient failures (e.g. the signing-in
            // peer still holds the lock) leave state unchanged — retried next period.
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            if (_state.CurrentValue != AuthState.SignedIn)
                return false;
            if (ReferenceEquals(_deadFamilyRecheckCts, owner))
                StopDeadFamilyRecheck(); // normally already stopped by AdoptInMemory — belt and braces
            return true;
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
            _signInRequiredReason = reason ?? "unknown";
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

            StopDeadFamilyRecheck(); // cancels the parked wake-up loop (02 §C3.3 — stops on disposal)
            _signInRequired.Dispose();
            _stateReadOnly.Dispose();
            _state.Dispose();
            _gate.Dispose();
        }
    }
}
