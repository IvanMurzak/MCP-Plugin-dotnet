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
    /// The outcome of an RFC 8693 token exchange (unified-machine-auth 04 §4). On success it
    /// carries the freshly minted plugin-family token material plus the response's <c>scope</c>
    /// and <c>sub</c> (the a5 exchange response contract); on failure only a
    /// <see cref="FailureReason"/>. Fails closed — never partial.
    /// </summary>
    public sealed class TokenExchangeResult
    {
        /// <summary>True when the exchange minted a plugin-family credential.</summary>
        public bool Succeeded { get; }

        /// <summary>The new plugin-plane access token (ES256 JWT). Non-null on success.</summary>
        public string? AccessToken { get; }

        /// <summary>The new plugin family's refresh token (RFC 8693 §2.2.1 documented exception — the a5 response always carries one).</summary>
        public string? RefreshToken { get; }

        /// <summary>Absolute expiry of <see cref="AccessToken"/> (from the response's <c>expires_in</c>).</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>The granted scope (<c>mcp:plugin</c>) as echoed by the server.</summary>
        public string? Scope { get; }

        /// <summary>The account id (<c>sub</c>) the minted family resolves to (O5 — no path writes a subject-less credential).</summary>
        public string? Subject { get; }

        /// <summary>The response's <c>issued_token_type</c> (the access-token URN).</summary>
        public string? IssuedTokenType { get; }

        /// <summary>A short human-facing reason when <see cref="Succeeded"/> is false. Never token material.</summary>
        public string? FailureReason { get; }

        TokenExchangeResult(bool succeeded, string? accessToken, string? refreshToken, DateTimeOffset? expiresAt,
            string? scope, string? subject, string? issuedTokenType, string? failureReason)
        {
            Succeeded = succeeded;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
            Scope = scope;
            Subject = subject;
            IssuedTokenType = issuedTokenType;
            FailureReason = failureReason;
        }

        /// <summary>A successful exchange.</summary>
        public static TokenExchangeResult Success(string accessToken, string? refreshToken, DateTimeOffset? expiresAt,
            string? scope, string? subject, string? issuedTokenType)
        {
            if (string.IsNullOrEmpty(accessToken))
                throw new ArgumentException("A successful exchange must carry a non-empty access token.", nameof(accessToken));
            return new TokenExchangeResult(true, accessToken, refreshToken, expiresAt, scope, subject, issuedTokenType, failureReason: null);
        }

        /// <summary>A failed exchange. Fail closed — no token.</summary>
        public static TokenExchangeResult Failure(string? reason = null)
            => new TokenExchangeResult(false, null, null, null, null, null, null, reason);
    }

    /// <summary>
    /// The RFC 8693 token-exchange seam (unified-machine-auth 04 §4): derives a plugin family from
    /// an agent access token, presenting the exchanging component's own <c>client_id</c>. Injected
    /// so tests (and the login-commit helper's tests) substitute a fake;
    /// <see cref="TokenExchangeClient"/> is the production implementation.
    /// </summary>
    public interface ITokenExchangeClient
    {
        /// <summary>
        /// The OAuth client id this exchanger presents — and therefore the id the minted plugin
        /// family is stamped with (design D8: written from the value actually presented).
        /// </summary>
        string ClientId { get; }

        /// <summary>
        /// Exchange <paramref name="subjectAccessToken"/> (a FRESH agent-plane ES256 access token —
        /// PATs are rejected by the server) for a plugin-family credential.
        /// <paramref name="serverTarget"/> null uses the implementation's default.
        /// </summary>
        Task<TokenExchangeResult> ExchangeAsync(string subjectAccessToken, string? serverTarget, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Production RFC 8693 token-exchange client (unified-machine-auth 04 §4, shape frozen by a5):
    /// POSTs to <c>{serverTarget}/oauth/token</c> with
    /// <c>grant_type=urn:ietf:params:oauth:grant-type:token-exchange</c>,
    /// <c>subject_token=&lt;agent access token&gt;</c>,
    /// <c>subject_token_type=urn:ietf:params:oauth:token-type:access_token</c>,
    /// <c>client_id=&lt;own id, ≤64 chars&gt;</c>, <c>scope=mcp:plugin</c>. <c>audience</c> and
    /// <c>resource</c> are deliberately OMITTED (the server accepts them only as the exact
    /// <c>urn:agd:hub</c>; omitting is the frozen-contract-safe choice). Applies the shared 15 s
    /// HTTP timeout. Fails closed; never logs token material.
    /// </summary>
    public sealed class TokenExchangeClient : ITokenExchangeClient
    {
        /// <summary>The RFC 8693 grant type URN (a5 frozen shape).</summary>
        public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

        /// <summary>The exact subject-token-type URN the server requires (a5 frozen shape).</summary>
        public const string SubjectTokenType = "urn:ietf:params:oauth:token-type:access_token";

        /// <summary>The scope requested for the derived family (a5: omit → defaults; anything else → <c>invalid_scope</c>).</summary>
        public const string PluginScope = "mcp:plugin";

        /// <summary>Maximum accepted <c>client_id</c> length (a5 frozen shape).</summary>
        public const int MaxClientIdLength = 64;

        /// <summary>Per-call HTTP timeout in ms — the shared contract value (04 §2).</summary>
        public const int HttpTimeoutMs = MachineCredentialLock.REFRESH_HTTP_TIMEOUT;

        static readonly HttpClient _sharedHttpClient = new HttpClient();

        readonly string _defaultServerTarget;
        readonly string _clientId;
        readonly HttpClient _httpClient;
        readonly Func<DateTimeOffset> _clock;
        readonly int _timeoutMs;

        /// <summary>
        /// <paramref name="defaultServerTarget"/> — the AS root used when a call passes no target.
        /// <paramref name="clientId"/> — this component's own OAuth client id (≤64 chars, a5).
        /// </summary>
        public TokenExchangeClient(string defaultServerTarget, string clientId, HttpClient? httpClient = null)
            : this(defaultServerTarget, clientId, httpClient, clock: null, timeoutMs: null)
        {
        }

        /// <summary>Test seam (InternalsVisibleTo: McpPlugin.Tests): override clock/timeout.</summary>
        internal TokenExchangeClient(string defaultServerTarget, string clientId, HttpClient? httpClient, Func<DateTimeOffset>? clock, int? timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("The exchanging component's own client id is required (RFC 8693, design O2).", nameof(clientId));
            if (clientId.Length > MaxClientIdLength)
                throw new ArgumentException("client_id must be at most " + MaxClientIdLength + " characters (a5 exchange contract).", nameof(clientId));

            _defaultServerTarget = HttpTokenRefresher.NormalizeServerTarget(defaultServerTarget) ?? string.Empty;
            _clientId = clientId;
            _httpClient = httpClient ?? _sharedHttpClient;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _timeoutMs = timeoutMs ?? HttpTimeoutMs;
        }

        /// <inheritdoc/>
        public string ClientId => _clientId;

        /// <inheritdoc/>
        public async Task<TokenExchangeResult> ExchangeAsync(string subjectAccessToken, string? serverTarget, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(subjectAccessToken))
                return TokenExchangeResult.Failure("no subject access token");

            var baseUrl = HttpTokenRefresher.NormalizeServerTarget(serverTarget) ?? _defaultServerTarget;
            if (string.IsNullOrEmpty(baseUrl))
                return TokenExchangeResult.Failure("no server target");

            cancellationToken.ThrowIfCancellationRequested();

            // The a5 frozen wire shape — exactly these five parameters; audience/resource omitted.
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", GrantType),
                new KeyValuePair<string, string>("subject_token", subjectAccessToken),
                new KeyValuePair<string, string>("subject_token_type", SubjectTokenType),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("scope", PluginScope),
            };

            using var timeout = new CancellationTokenSource(_timeoutMs);
            try
            {
                using var content = new FormUrlEncodedContent(form);
                using var response = await _httpClient.PostAsync(baseUrl + "/oauth/token", content, timeout.Token).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseExchangeResponse(response.IsSuccessStatusCode, (int)response.StatusCode, json, _clock);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return TokenExchangeResult.Failure("token exchange timed out after " + _timeoutMs + " ms");
            }
            catch (Exception ex)
            {
                return TokenExchangeResult.Failure(ex.Message); // never token material
            }
        }

        /// <summary>
        /// Parse an a5 exchange response (pure, unit-testable). Response keys:
        /// <c>access_token</c>, <c>issued_token_type</c>, <c>token_type=Bearer</c>,
        /// <c>expires_in</c>, <c>refresh_token</c>, <c>scope</c>, <c>sub</c>.
        /// </summary>
        internal static TokenExchangeResult ParseExchangeResponse(bool isSuccessStatusCode, int statusCode, string json, Func<DateTimeOffset> clock)
        {
            string? accessToken = null;
            string? refreshToken = null;
            long expiresInSeconds = 0;
            string? scope = null;
            string? subject = null;
            string? issuedTokenType = null;
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
                    if (root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String)
                        scope = sc.GetString();
                    if (root.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String)
                        subject = sub.GetString();
                    if (root.TryGetProperty("issued_token_type", out var itt) && itt.ValueKind == JsonValueKind.String)
                        issuedTokenType = itt.GetString();
                    if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                        error = err.GetString();
                    if (root.TryGetProperty("error_description", out var desc) && desc.ValueKind == JsonValueKind.String)
                        errorDescription = desc.GetString();
                }
            }
            catch (JsonException)
            {
                return TokenExchangeResult.Failure("malformed exchange response (HTTP " + statusCode + ")");
            }

            if (!isSuccessStatusCode || string.IsNullOrEmpty(accessToken))
            {
                var reason = error != null
                    ? (errorDescription != null ? error + ": " + errorDescription : error)
                    : "token exchange failed (HTTP " + statusCode + ")";
                return TokenExchangeResult.Failure(reason);
            }

            DateTimeOffset? expiresAt = expiresInSeconds > 0
                ? clock().AddSeconds(expiresInSeconds)
                : (DateTimeOffset?)null;
            return TokenExchangeResult.Success(accessToken!, refreshToken, expiresAt, scope, subject, issuedTokenType);
        }
    }
}
