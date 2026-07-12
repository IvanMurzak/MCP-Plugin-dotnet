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
    /// The outcome of a token refresh (mcp-authorize b7). On success it carries the freshly minted access
    /// token (and, if the AS rotated it, a new refresh token) plus the new absolute expiry; on failure it
    /// carries only a <see cref="FailureReason"/>. Never carries partial success — a failed refresh leaves
    /// the caller to surface the "sign in again" state and keep the old (rejected) credential untouched.
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

        TokenRefreshResult(bool succeeded, string? accessToken, string? refreshToken, DateTimeOffset? expiresAt, string? failureReason)
        {
            Succeeded = succeeded;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpiresAt = expiresAt;
            FailureReason = failureReason;
        }

        /// <summary>A successful refresh. <paramref name="refreshToken"/> null ⇒ the AS did not rotate it (keep the old one).</summary>
        public static TokenRefreshResult Success(string accessToken, string? refreshToken = null, DateTimeOffset? expiresAt = null)
        {
            if (string.IsNullOrEmpty(accessToken))
                throw new ArgumentException("A successful refresh must carry a non-empty access token.", nameof(accessToken));
            return new TokenRefreshResult(true, accessToken, refreshToken, expiresAt, failureReason: null);
        }

        /// <summary>A failed refresh (expired/revoked refresh token, AS unreachable, …). Fail closed — no token.</summary>
        public static TokenRefreshResult Failure(string? reason = null)
            => new TokenRefreshResult(false, accessToken: null, refreshToken: null, expiresAt: null, failureReason: reason);
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
        /// </summary>
        Task<TokenRefreshResult> RefreshAsync(string refreshToken, string? serverTarget, CancellationToken cancellationToken = default);
    }
}
