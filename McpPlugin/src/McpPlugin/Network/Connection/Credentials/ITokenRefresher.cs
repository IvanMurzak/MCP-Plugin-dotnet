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

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// How a failed refresh should be classified by the caller (unified-machine-auth 04 §3.5). The
    /// distinction is behavioral: <see cref="InvalidGrant"/> and <see cref="InvalidTarget"/> are
    /// the only terminal kinds — the only ones that may declare a credential family dead (after
    /// the mandatory post-failure re-read); everything else is retryable and must never destroy
    /// local state.
    /// </summary>
    public enum TokenRefreshFailureKind
    {
        /// <summary>
        /// A retryable failure: network error, timeout, 5xx, rate limit, malformed response. The
        /// caller keeps the existing credential and may try again later.
        /// </summary>
        Transient = 0,

        /// <summary>
        /// The authorization server answered <c>invalid_grant</c> — the refresh token is expired,
        /// revoked, or reuse-revoked. After a post-failure re-read confirms no peer rotated it, the
        /// family is dead: surface sign-in-required, never loop, never delete other families.
        /// </summary>
        InvalidGrant = 1,

        /// <summary>
        /// The authorization server answered <c>invalid_target</c> (RFC 8707 §2): the requested
        /// resource is invalid, missing, unknown, or malformed. Terminal like
        /// <see cref="InvalidGrant"/>, but a server-side audience/resource mismatch — re-auth does
        /// not reliably cure it, so the caller surfaces a configuration error instead of
        /// retry-looping. Defensive hardening today: our refresh requests send no <c>resource</c>
        /// parameter, so current clients cannot receive this error; it exists so a future client
        /// that adopts RFC 8707 resource indicators inherits sane terminal behavior.
        /// </summary>
        InvalidTarget = 2,
    }

    /// <summary>
    /// The outcome of a token refresh (mcp-authorize b7). On success it carries the freshly minted access
    /// token (and, if the AS rotated it, a new refresh token) plus the new absolute expiry; on failure it
    /// carries only a <see cref="FailureReason"/> and a <see cref="FailureKind"/>. Never carries partial
    /// success — a failed refresh leaves the caller to surface the "sign in again" state and keep the old
    /// (rejected) credential untouched.
    /// </summary>
    public sealed class TokenRefreshResult
    {
        /// <summary>True when a new access token was obtained.</summary>
        public bool Succeeded { get; }

        /// <summary>The new short-lived access token (an ES256 JWT). Non-null on success.</summary>
        public string? AccessToken { get; }

        /// <summary>The new refresh token when the AS rotated it; null keeps the caller's existing refresh token.</summary>
        public string? RefreshToken { get; }

        /// <summary>Absolute expiry of <see cref="AccessToken"/> (used to schedule the next proactive refresh).</summary>
        public DateTimeOffset? ExpiresAt { get; }

        /// <summary>A short human-facing reason when <see cref="Succeeded"/> is false (e.g. "refresh token expired").</summary>
        public string? FailureReason { get; }

        /// <summary>
        /// Classification of the failure when <see cref="Succeeded"/> is false (unified-machine-auth
        /// 04 §3.5): only <see cref="TokenRefreshFailureKind.InvalidGrant"/> may lead the caller to
        /// declare the credential family dead. <see cref="TokenRefreshFailureKind.Transient"/> for a
        /// successful result (meaningless there).
        /// </summary>
        public TokenRefreshFailureKind FailureKind { get; }

        TokenRefreshResult(bool succeeded, string? accessToken, string? refreshToken, DateTimeOffset? expiresAt, string? failureReason, TokenRefreshFailureKind failureKind)
        {
            Succeeded = succeeded;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
            FailureReason = failureReason;
            FailureKind = failureKind;
        }

        /// <summary>A successful refresh. <paramref name="refreshToken"/> null ⇒ the AS did not rotate it (keep the old one).</summary>
        public static TokenRefreshResult Success(string accessToken, string? refreshToken = null, DateTimeOffset? expiresAt = null)
        {
            if (string.IsNullOrEmpty(accessToken))
                throw new ArgumentException("A successful refresh must carry a non-empty access token.", nameof(accessToken));
            return new TokenRefreshResult(true, accessToken, refreshToken, expiresAt, failureReason: null, TokenRefreshFailureKind.Transient);
        }

        /// <summary>
        /// A failed refresh (expired/revoked refresh token, AS unreachable, …). Fail closed — no token.
        /// Classified <see cref="TokenRefreshFailureKind.Transient"/>; use the two-argument overload to
        /// mark a definitive <c>invalid_grant</c>.
        /// </summary>
        public static TokenRefreshResult Failure(string? reason = null)
            => new TokenRefreshResult(false, accessToken: null, refreshToken: null, expiresAt: null, failureReason: reason, TokenRefreshFailureKind.Transient);

        /// <summary>A failed refresh with an explicit classification (see <see cref="TokenRefreshFailureKind"/>).</summary>
        public static TokenRefreshResult Failure(string? reason, TokenRefreshFailureKind kind)
            => new TokenRefreshResult(false, accessToken: null, refreshToken: null, expiresAt: null, failureReason: reason, failureKind: kind);
    }

    /// <summary>
    /// One refresh attempt's inputs (unified-machine-auth 04 §3) — the v2 API of
    /// <see cref="ITokenRefresher"/>. Carries the credential family's context so the refresher can
    /// present the <b>family's stored <c>clientId</c></b> (design D8; the P0 the old two-string API
    /// could not express). Deliberately has NO scope and NO resource member: a refresh request must
    /// omit both entirely so the server falls back to the stored grant — sending a component default
    /// would permanently narrow an agent family (04 §3.3, P0-3).
    /// </summary>
    public sealed class TokenRefreshRequest
    {
        /// <summary>
        /// <paramref name="refreshToken"/> — the family's current refresh token (required).
        /// <paramref name="serverTarget"/> — the enrolled AS/hub target the credential was issued
        /// for, or null to use the refresher's default. <paramref name="clientId"/> — the
        /// <b>stored</b> <c>clientId</c> the family was minted under (04 §3.2); null means the
        /// family does not know its id (a v1-adopted <c>families.legacy</c> credential, 04 §3.7)
        /// and the refresher presents its component-default id instead.
        /// </summary>
        public TokenRefreshRequest(string refreshToken, string? serverTarget = null, string? clientId = null)
        {
            if (string.IsNullOrEmpty(refreshToken))
                throw new ArgumentException("A refresh request must carry a non-empty refresh token.", nameof(refreshToken));
            RefreshToken = refreshToken;
            ServerTarget = serverTarget;
            ClientId = clientId;
        }

        /// <summary>The family's current refresh token. Never logged.</summary>
        public string RefreshToken { get; }

        /// <summary>The enrolled AS/hub target the credential was issued for, or null for the refresher's default.</summary>
        public string? ServerTarget { get; }

        /// <summary>
        /// The stored <c>clientId</c> the family was minted under, or null for a legacy family of
        /// unknown id (the refresher then presents its component default — 04 §3.7).
        /// </summary>
        public string? ClientId { get; }
    }

    /// <summary>
    /// Exchanges a refresh token for a fresh access token against the ai-game.dev authorization server
    /// (<c>grant_type=refresh_token</c> at <c>/oauth/token</c>; design 03 Flow B). This is the ONE piece of
    /// the connection layer that talks to the AS over HTTP, so it is an injected seam: the shared library
    /// defines the contract, and each engine plugin / CLI (which already owns the device-flow HTTP client)
    /// provides the concrete implementation. Tests substitute a fake. Implementations MUST fail closed —
    /// any error returns <see cref="TokenRefreshResult.Failure"/>, never a partial or fabricated token —
    /// and MUST NOT log token material.
    /// </summary>
    public interface ITokenRefresher
    {
        /// <summary>
        /// Attempt to mint a new access token from <paramref name="refreshToken"/>.
        /// <paramref name="serverTarget"/> is the enrolled hub/AS target the credential was issued for
        /// (hosted vs local), or null to use the implementation's default.
        /// <para><b>Legacy v1 API (pre unified-machine-auth):</b> carries no family context, so an
        /// implementation can only present its component-default <c>clientId</c>. Kept abstract so
        /// existing engine implementations keep compiling; new callers use the
        /// <see cref="RefreshAsync(TokenRefreshRequest, CancellationToken)"/> overload, and engines
        /// migrate to the shared <see cref="HttpTokenRefresher"/> in their adoption tasks.</para>
        /// </summary>
        Task<TokenRefreshResult> RefreshAsync(string refreshToken, string? serverTarget, CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempt to mint a new access token for one credential family (unified-machine-auth
        /// 04 §3). The request carries the family's stored <c>clientId</c>, which the
        /// implementation MUST present verbatim when set (design D8 — presenting a different id
        /// cross-mints the family); scope and resource MUST be omitted from the wire request
        /// entirely (04 §3.3, P0-3).
        /// <para><b>Compat default:</b> this default implementation delegates to the legacy
        /// two-string API, DROPPING the request's <c>clientId</c> — i.e. a legacy engine refresher
        /// invoked through it keeps its status-quo (component-default) behavior. That is a
        /// transition bridge only: the shared <see cref="HttpTokenRefresher"/> overrides it with
        /// the family-correct implementation, and the engine cascades (d1/e1/f1) replace legacy
        /// refreshers with it.</para>
        /// </summary>
        Task<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return RefreshAsync(request.RefreshToken, request.ServerTarget, cancellationToken);
        }
    }
}
