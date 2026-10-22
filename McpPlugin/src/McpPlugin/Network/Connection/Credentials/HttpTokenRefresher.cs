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
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The ONE shared <see cref="ITokenRefresher"/> for all engine plugins (unified-machine-auth
    /// 04 §3, task b3) — replaces the per-engine refreshers (Unity's <c>UnityTokenRefresher</c>
    /// et al., which send a component-default <c>scope</c> on every refresh: the P0-3 defect).
    /// Exchanges a refresh token for a fresh access token at <c>{serverTarget}/oauth/token</c>
    /// (<c>grant_type=refresh_token</c>), obeying the shared refresh rules:
    /// <list type="bullet">
    ///   <item><b>Stored <c>clientId</c> (04 §3.2):</b> the request presents the family's stored
    ///   <c>clientId</c> verbatim when the <see cref="TokenRefreshRequest"/> carries one; only a
    ///   legacy family of unknown id (<see cref="TokenRefreshRequest.ClientId"/> null) falls back
    ///   to the component default (04 §3.7).</item>
    ///   <item><b>No scope, no resource (04 §3.3, P0-3):</b> the wire request omits both
    ///   parameters entirely — the server falls back to the stored grant; sending a component
    ///   default would permanently narrow an agent family.</item>
    ///   <item><b>Rotation without a new refresh token (04 §3.4):</b> a success response missing
    ///   <c>refresh_token</c> yields <see cref="TokenRefreshResult.RefreshToken"/> null, telling
    ///   the caller to keep the previous one.</item>
    ///   <item><b>Rate discipline (04 §3.6):</b> at most one network attempt per family per skew
    ///   window per refresher instance (families are keyed by the refresh token presented — each
    ///   family owns a distinct rotation chain — together with the client id presented, so the
    ///   provider's client-id fallback for a family with no stored id reaches the server under each
    ///   candidate id instead of replaying the first mismatch). A repeat call inside the window shares/replays the
    ///   window's single attempt (in flight, succeeded, or failed with a terminal
    ///   <c>invalid_grant</c>/<c>invalid_target</c>) instead of hitting the AS again, so
    ///   reactive-401 storms cannot hammer the token endpoint. A <b>transient</b> failure that may
    ///   be a lost response (timeout, network error, 5xx — not a 4xx answer) is the exception: it
    ///   is replayed only for <see cref="TransientFailureCooldown"/>, then the next
    ///   caller presents the SAME token again. A transient failure is often a lost response to a
    ///   refresh the server already committed (it rotated the token); the server absorbs a retry
    ///   of that token only inside its short rotation grace window (ai-game.dev: 30 s) and treats
    ///   a later one as reuse, revoking the whole family — so the retry must not wait out the
    ///   window.</item>
    ///   <item><b>15 s HTTP timeout (04 §2):</b> <see cref="MachineCredentialLock.REFRESH_HTTP_TIMEOUT"/>
    ///   is applied per call — explicitly, because .NET's 100 s default would outlive
    ///   <see cref="MachineCredentialLock.LOCK_STALE_MS"/> and let a live lock holder be declared
    ///   stale mid-refresh. A timeout is a <see cref="TokenRefreshFailureKind.Transient"/> failure,
    ///   never an unclassified exception.</item>
    ///   <item><b>Terminal-error classification (04 §3.5):</b> an OAuth error response of exactly
    ///   <c>invalid_grant</c> is returned as <see cref="TokenRefreshFailureKind.InvalidGrant"/>,
    ///   and exactly <c>invalid_target</c> (RFC 8707 §2) as
    ///   <see cref="TokenRefreshFailureKind.InvalidTarget"/> — defensive hardening: our refresh
    ///   requests send no <c>resource</c>, so current clients cannot receive it. An
    ///   <c>invalid_grant</c> whose description names a client-id mismatch
    ///   (<see cref="RefreshClientIdFallback.ClientIdMismatchDescription"/>) is
    ///   <see cref="TokenRefreshFailureKind.ClientIdMismatch"/> instead — the token is live for
    ///   its real mint client, so it must never read as a dead family. Every other
    ///   failure (network, 5xx, other OAuth errors, malformed JSON) is
    ///   <see cref="TokenRefreshFailureKind.Transient"/>.</item>
    /// </list>
    /// Fails closed — any error returns <see cref="TokenRefreshResult.Failure(string?)"/>, never a
    /// partial or fabricated token — and never logs token material.
    /// </summary>
    public sealed class HttpTokenRefresher : ITokenRefresher
    {
        /// <summary>
        /// Per-call HTTP timeout in ms — the shared cross-language contract value
        /// (<see cref="MachineCredentialLock.REFRESH_HTTP_TIMEOUT"/> = 15 s, 04 §2).
        /// </summary>
        public const int HttpTimeoutMs = MachineCredentialLock.REFRESH_HTTP_TIMEOUT;

        /// <summary>
        /// Default rate-discipline window (04 §3.6): one network attempt per family per this window,
        /// matching the proactive-refresh skew (<see cref="PluginCredentialProvider.DefaultRefreshSkew"/>).
        /// </summary>
        public static readonly TimeSpan DefaultAttemptWindow = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How long a TRANSIENT failure that may be a lost response (timeout, network error, 5xx,
        /// malformed 2xx body) is replayed to later callers before the next call hits the network
        /// again with the same refresh token. It only stops a hot loop and must stay well under the
        /// server's rotation grace window (ai-game.dev <c>OAUTH_ROTATION_GRACE_WINDOW_SECONDS</c> =
        /// 30 s) — see the class docs. A 4xx answer (429, <c>invalid_client</c>, …) is a decided
        /// response, not a lost one, and keeps the full window.
        /// </summary>
        internal static readonly TimeSpan TransientFailureCooldown = TimeSpan.FromSeconds(2);

        static readonly HttpClient _sharedHttpClient = new HttpClient();

        readonly string _defaultServerTarget;
        readonly string _componentClientId;
        readonly HttpClient _httpClient;
        readonly Func<DateTimeOffset> _clock;
        readonly TimeSpan _attemptWindow;
        readonly int _timeoutMs;

        // Rate discipline (04 §3.6): (client id presented, refresh token) → the window's single
        // shared attempt (see AttemptKey). Guarded by
        // _attemptGate; pruned on every call (a process holds ≤3 families, so this stays tiny).
        readonly object _attemptGate = new object();
        readonly Dictionary<string, AttemptEntry> _attempts = new Dictionary<string, AttemptEntry>();

        sealed class AttemptEntry
        {
            public DateTimeOffset StartedAt;
            public Task<TokenRefreshResult> Task = null!;

            /// <summary>Set (under <c>_attemptGate</c>) when the attempt ended in a transient failure that may be a lost response.</summary>
            public DateTimeOffset? TransientFailedAt;
        }

        /// <summary>
        /// <paramref name="defaultServerTarget"/> — the AS root used when a request carries no
        /// <see cref="TokenRefreshRequest.ServerTarget"/> (e.g. <c>https://ai-game.dev</c>).
        /// <paramref name="componentClientId"/> — this component's own OAuth client id, presented
        /// ONLY for legacy families of unknown id (04 §3.7); a family with a stored id always wins.
        /// <paramref name="httpClient"/> — optional injection for tests; defaults to a shared client.
        /// </summary>
        public HttpTokenRefresher(string defaultServerTarget, string componentClientId, HttpClient? httpClient = null)
            : this(defaultServerTarget, componentClientId, httpClient, clock: null, attemptWindow: null, timeoutMs: null)
        {
        }

        /// <summary>
        /// Test seam (InternalsVisibleTo: McpPlugin.Tests): override the clock, the rate-discipline
        /// window, and the HTTP timeout so unit tests do not consume real wall-clock time.
        /// Production callers use the public constructor, which pins the contract values.
        /// </summary>
        internal HttpTokenRefresher(
            string defaultServerTarget,
            string componentClientId,
            HttpClient? httpClient,
            Func<DateTimeOffset>? clock,
            TimeSpan? attemptWindow,
            int? timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(componentClientId))
                throw new ArgumentException("The component's own client id is required (used for legacy families of unknown id, 04 §3.7).", nameof(componentClientId));

            _defaultServerTarget = NormalizeServerTarget(defaultServerTarget) ?? string.Empty;
            _componentClientId = componentClientId;
            _httpClient = httpClient ?? _sharedHttpClient;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _attemptWindow = attemptWindow ?? DefaultAttemptWindow;
            _timeoutMs = timeoutMs ?? HttpTimeoutMs;
        }

        /// <summary>The component-default client id presented for legacy families of unknown id (04 §3.7).</summary>
        public string ComponentClientId => _componentClientId;

        /// <summary>
        /// Legacy v1 API: no family context, so the component-default <c>clientId</c> is presented
        /// (the 04 §3.7 legacy rule) and — unlike the pre-b3 engine refreshers — <c>scope</c> is
        /// still omitted from the wire request.
        /// </summary>
        public Task<TokenRefreshResult> RefreshAsync(string refreshToken, string? serverTarget, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(refreshToken))
                return Task.FromResult(TokenRefreshResult.Failure("no refresh token"));
            return RefreshAsync(new TokenRefreshRequest(refreshToken, serverTarget, clientId: null), cancellationToken);
        }

        /// <summary>The family-aware refresh (see the class docs for the rules it enforces).</summary>
        public Task<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            var baseUrl = NormalizeServerTarget(request.ServerTarget) ?? _defaultServerTarget;
            if (string.IsNullOrEmpty(baseUrl))
                return Task.FromResult(TokenRefreshResult.Failure("no server target"));

            // Rate discipline (04 §3.6): inside the window, every caller presenting the same
            // refresh token shares the window's single attempt (in-flight or memoized; a transient
            // failure only for TransientFailureCooldown). The shared task deliberately ignores the
            // individual caller's CancellationToken — one caller aborting must not kill the attempt
            // for its peers — and is bounded by the HTTP timeout, so nobody waits more than ~15 s.
            lock (_attemptGate)
            {
                PruneExpiredAttempts();

                var key = AttemptKey(request);
                if (_attempts.TryGetValue(key, out var existing))
                    return existing.Task;

                var entry = new AttemptEntry { StartedAt = _clock() };
                entry.Task = RunAttemptAsync(entry, baseUrl, request);
                _attempts[key] = entry;
                return entry.Task;
            }
        }

        // The client id this request presents on the wire: the family's stored id, else the
        // component default (04 §3.7).
        string PresentedClientId(TokenRefreshRequest request) => request.ClientId ?? _componentClientId;

        // A family is identified by its refresh token; the presented client id is part of the key
        // because the server answers each (token, id) pair independently — a client-id mismatch for
        // one id says nothing about another (04 §3 rule 7). A newline never occurs in either part.
        string AttemptKey(TokenRefreshRequest request) => PresentedClientId(request) + "\n" + request.RefreshToken;

        async Task<TokenRefreshResult> RunAttemptAsync(AttemptEntry entry, string baseUrl, TokenRefreshRequest request)
        {
            var (result, mayBeLostResponse) = await RefreshOverHttpAsync(baseUrl, request).ConfigureAwait(false);
            if (mayBeLostResponse)
            {
                // Start the short cooldown from the moment the failure is known, not from the
                // attempt start: a 15 s timeout must not leave a stale failure memoized afterwards.
                lock (_attemptGate)
                    entry.TransientFailedAt = _clock();
            }
            else if (!result.Succeeded && result.FailureKind == TokenRefreshFailureKind.Transient)
            {
                result.MarkDecidedAnswer(); // a 4xx answer — the server decided, nothing was lost
            }
            return result;
        }

        // MUST be called with _attemptGate held.
        bool IsExpired(AttemptEntry entry, DateTimeOffset now)
            => now - entry.StartedAt >= _attemptWindow
                || (entry.TransientFailedAt.HasValue && now - entry.TransientFailedAt.Value >= TransientFailureCooldown);

        // MUST be called with _attemptGate held.
        void PruneExpiredAttempts()
        {
            List<string>? expired = null;
            var now = _clock();
            foreach (var pair in _attempts)
            {
                if (IsExpired(pair.Value, now))
                    (expired ??= new List<string>()).Add(pair.Key);
            }
            if (expired != null)
            {
                foreach (var key in expired)
                    _attempts.Remove(key);
            }
        }

        // MayBeLostResponse: a transient failure whose answer may have been lost after the server
        // committed a rotation (timeout, network error, 5xx, a 2xx without a usable body) — the
        // short-cooldown case. A 4xx is a decided answer and keeps the full rate-discipline window.
        async Task<(TokenRefreshResult Result, bool MayBeLostResponse)> RefreshOverHttpAsync(string baseUrl, TokenRefreshRequest request)
        {
            // The 04 §3 wire contract: grant_type + refresh_token + client_id and NOTHING else.
            // No scope (P0-3: a component default permanently narrows an agent family) and no
            // resource — the server falls back to the stored grant for both.
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", request.RefreshToken),
                new KeyValuePair<string, string>("client_id", PresentedClientId(request)),
            };

            using var timeout = new CancellationTokenSource(_timeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(baseUrl + "/oauth/token", content, timeout.Token).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;
                var result = ParseTokenResponse(response.IsSuccessStatusCode, statusCode, json, _clock);
                var mayBeLost = !result.Succeeded
                    && result.FailureKind == TokenRefreshFailureKind.Transient
                    && (statusCode >= 500 || statusCode < 400);
                return (result, mayBeLost);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // The 15 s contract timeout fired (04 §2) — a transient failure, never an
                // unclassified exception: the caller keeps its credential and retries later.
                return (TokenRefreshResult.Failure("refresh HTTP call timed out after " + _timeoutMs + " ms"), true);
            }
            catch (Exception ex)
            {
                return (TokenRefreshResult.Failure(ex.Message), true); // never token material
            }
        }

        /// <summary>
        /// Parse an <c>/oauth/token</c> refresh response (pure, unit-testable). A success without
        /// <c>refresh_token</c> keeps <see cref="TokenRefreshResult.RefreshToken"/> null (04 §3.4);
        /// an OAuth error of exactly <c>invalid_grant</c> is classified
        /// <see cref="TokenRefreshFailureKind.InvalidGrant"/> (or
        /// <see cref="TokenRefreshFailureKind.ClientIdMismatch"/> when its description names a
        /// client-id mismatch), exactly <c>invalid_target</c>
        /// (RFC 8707 §2) is <see cref="TokenRefreshFailureKind.InvalidTarget"/>, everything else
        /// transient. The raw <c>error</c> (and <c>error_description</c> when present) is preserved
        /// verbatim in <see cref="TokenRefreshResult.FailureReason"/> so telemetry keeps distinct
        /// reason strings per error code.
        /// </summary>
        internal static TokenRefreshResult ParseTokenResponse(bool isSuccessStatusCode, int statusCode, string json, Func<DateTimeOffset> clock)
        {
            string? accessToken = null;
            string? refreshToken = null;
            long expiresInSeconds = 0;
            string? error = null;
            string? errorDescription = null;

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String)
                        accessToken = at.GetString();
                    if (root.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String)
                        refreshToken = rt.GetString();
                    if (root.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number)
                        expiresInSeconds = exp.GetInt64();
                    if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                        error = err.GetString();
                    if (root.TryGetProperty("error_description", out var desc) && desc.ValueKind == JsonValueKind.String)
                        errorDescription = desc.GetString();
                }
            }
            catch (JsonException)
            {
                return TokenRefreshResult.Failure("malformed token response (HTTP " + statusCode + ")");
            }

            if (!isSuccessStatusCode || string.IsNullOrEmpty(accessToken))
            {
                var kind = string.Equals(error, "invalid_grant", StringComparison.Ordinal)
                    ? (RefreshClientIdFallback.IsClientIdMismatchText(errorDescription)
                        ? TokenRefreshFailureKind.ClientIdMismatch
                        : TokenRefreshFailureKind.InvalidGrant)
                    : string.Equals(error, "invalid_target", StringComparison.Ordinal)
                        ? TokenRefreshFailureKind.InvalidTarget
                        : TokenRefreshFailureKind.Transient;
                var reason = error != null
                    ? (errorDescription != null ? error + ": " + errorDescription : error)
                    : "refresh failed (HTTP " + statusCode + ")";
                return TokenRefreshResult.Failure(reason, kind);
            }

            DateTimeOffset? expiresAt = expiresInSeconds > 0
                ? clock().AddSeconds(expiresInSeconds)
                : (DateTimeOffset?)null;
            return TokenRefreshResult.Success(accessToken!, refreshToken, expiresAt);
        }

        /// <summary>
        /// Normalize a stored server target to the AS root: trim whitespace, a trailing slash, and
        /// a trailing <c>/mcp</c> hub segment so <c>/oauth/token</c> resolves on the
        /// authorization-server root (same rule as the engines' historical refreshers).
        /// </summary>
        internal static string? NormalizeServerTarget(string? serverTarget)
        {
            if (string.IsNullOrWhiteSpace(serverTarget))
                return null;
            var s = serverTarget!.Trim().TrimEnd('/');
            if (s.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - "/mcp".Length);
            return s;
        }
    }
}
