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
    ///   family owns a distinct rotation chain). A repeat call inside the window shares/replays the
    ///   window's single attempt instead of hitting the AS again, so reactive-401 storms cannot
    ///   hammer the token endpoint.</item>
    ///   <item><b>15 s HTTP timeout (04 §2):</b> <see cref="MachineCredentialLock.REFRESH_HTTP_TIMEOUT"/>
    ///   is applied per call — explicitly, because .NET's 100 s default would outlive
    ///   <see cref="MachineCredentialLock.LOCK_STALE_MS"/> and let a live lock holder be declared
    ///   stale mid-refresh. A timeout is a <see cref="TokenRefreshFailureKind.Transient"/> failure,
    ///   never an unclassified exception.</item>
    ///   <item><b>Terminal-error classification (04 §3.5):</b> an OAuth error response of exactly
    ///   <c>invalid_grant</c> is returned as <see cref="TokenRefreshFailureKind.InvalidGrant"/>,
    ///   and exactly <c>invalid_target</c> (RFC 8707 §2) as
    ///   <see cref="TokenRefreshFailureKind.InvalidTarget"/> — defensive hardening: our refresh
    ///   requests send no <c>resource</c>, so current clients cannot receive it; every other
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

        static readonly HttpClient _sharedHttpClient = new HttpClient();

        readonly string _defaultServerTarget;
        readonly string _componentClientId;
        readonly HttpClient _httpClient;
        readonly Func<DateTimeOffset> _clock;
        readonly TimeSpan _attemptWindow;
        readonly int _timeoutMs;

        // Rate discipline (04 §3.6): refresh-token → the window's single shared attempt. Guarded by
        // _attemptGate; pruned on every call (a process holds ≤3 families, so this stays tiny).
        readonly object _attemptGate = new object();
        readonly Dictionary<string, AttemptEntry> _attempts = new Dictionary<string, AttemptEntry>();

        sealed class AttemptEntry
        {
            public DateTimeOffset StartedAt;
            public Task<TokenRefreshResult> Task = null!;
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
            // refresh token shares the window's single attempt (in-flight or memoized). The shared
            // task deliberately ignores the individual caller's CancellationToken — one caller
            // aborting must not kill the attempt for its peers — and is bounded by the HTTP
            // timeout, so nobody waits more than ~15 s.
            lock (_attemptGate)
            {
                PruneExpiredAttempts();

                if (_attempts.TryGetValue(request.RefreshToken, out var existing)
                    && _clock() - existing.StartedAt < _attemptWindow)
                    return existing.Task;

                var entry = new AttemptEntry { StartedAt = _clock() };
                entry.Task = RefreshOverHttpAsync(baseUrl, request);
                _attempts[request.RefreshToken] = entry;
                return entry.Task;
            }
        }

        // MUST be called with _attemptGate held.
        void PruneExpiredAttempts()
        {
            List<string>? expired = null;
            var now = _clock();
            foreach (var pair in _attempts)
            {
                if (now - pair.Value.StartedAt >= _attemptWindow)
                    (expired ??= new List<string>()).Add(pair.Key);
            }
            if (expired != null)
            {
                foreach (var key in expired)
                    _attempts.Remove(key);
            }
        }

        async Task<TokenRefreshResult> RefreshOverHttpAsync(string baseUrl, TokenRefreshRequest request)
        {
            // The 04 §3 wire contract: grant_type + refresh_token + client_id and NOTHING else.
            // No scope (P0-3: a component default permanently narrows an agent family) and no
            // resource — the server falls back to the stored grant for both.
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", request.RefreshToken),
                new KeyValuePair<string, string>("client_id", request.ClientId ?? _componentClientId),
            };

            using var timeout = new CancellationTokenSource(_timeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(baseUrl + "/oauth/token", content, timeout.Token).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseTokenResponse(response.IsSuccessStatusCode, (int)response.StatusCode, json, _clock);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // The 15 s contract timeout fired (04 §2) — a transient failure, never an
                // unclassified exception: the caller keeps its credential and retries later.
                return TokenRefreshResult.Failure("refresh HTTP call timed out after " + _timeoutMs + " ms");
            }
            catch (Exception ex)
            {
                return TokenRefreshResult.Failure(ex.Message); // never token material
            }
        }

        /// <summary>
        /// Parse an <c>/oauth/token</c> refresh response (pure, unit-testable). A success without
        /// <c>refresh_token</c> keeps <see cref="TokenRefreshResult.RefreshToken"/> null (04 §3.4);
        /// an OAuth error of exactly <c>invalid_grant</c> is classified
        /// <see cref="TokenRefreshFailureKind.InvalidGrant"/>, exactly <c>invalid_target</c>
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
                    ? TokenRefreshFailureKind.InvalidGrant
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
