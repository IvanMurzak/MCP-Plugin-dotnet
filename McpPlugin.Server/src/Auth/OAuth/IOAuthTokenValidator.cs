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
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Which authorization plane a token is being validated for (auth-fixes B11). The two planes accept
    /// DIFFERENT audiences, which is the whole point of the separation:
    /// <list type="bullet">
    ///   <item><see cref="Agent"/> — an AI-agent MCP request. Strict: the token <c>aud</c> MUST be the
    ///         canonical resource-server id (<c>--public-url</c>, e.g. <c>https://ai-game.dev/mcp</c>).
    ///         A plugin hub-token (<c>aud=urn:agd:hub</c>) is REJECTED here.</item>
    ///   <item><see cref="Plugin"/> — an engine-plugin hub registration. The token <c>aud</c> must be in
    ///         the plugin-plane allow-list (the plugin audience <c>urn:agd:hub</c>, OR the canonical
    ///         resource id when the token also carries the <c>mcp:plugin</c> scope). An agent token
    ///         (<c>aud=</c>canonical + <c>scope=mcp:agent</c>) can NEVER register as a plugin instance.</item>
    /// </list>
    /// </summary>
    public enum TokenValidationPlane
    {
        /// <summary>AI-agent MCP request plane — strict canonical-resource <c>aud</c> (the default/legacy behavior).</summary>
        Agent = 0,

        /// <summary>Engine-plugin hub-registration plane — accepts the plugin audience <c>urn:agd:hub</c> via a strict allow-list.</summary>
        Plugin = 1,
    }

    /// <summary>
    /// Validates a presented bearer token in OAuth resource-server mode (mcp-authorize b2): ES256
    /// JWTs against the AS JWKS with strict <c>iss</c>/<c>aud</c>/<c>exp</c>, opaque tokens via
    /// introspection. Always fails closed.
    /// </summary>
    public interface IOAuthTokenValidator
    {
        /// <summary>Validate a token for the <see cref="TokenValidationPlane.Agent"/> plane (strict canonical <c>aud</c>).</summary>
        Task<OAuthValidationResult> ValidateAsync(string token, CancellationToken cancellationToken);

        /// <summary>
        /// Validate a token for a specific authorization <paramref name="plane"/> (auth-fixes B11). The
        /// plane selects which <c>aud</c> values are accepted; every other check (ES256/JWKS, iss,
        /// exp/nbf, opaque introspection) is identical across planes. Plane separation is enforced
        /// here: an agent token never validates on the plugin plane and vice-versa.
        /// </summary>
        Task<OAuthValidationResult> ValidateAsync(string token, TokenValidationPlane plane, CancellationToken cancellationToken);
    }

    /// <summary>Outcome of resource-server token validation.</summary>
    public sealed class OAuthValidationResult
    {
        public bool Succeeded { get; }
        public string? FailureReason { get; }
        public string? Subject { get; }
        public string? Scope { get; }
        public string? ClientId { get; }

        /// <summary><c>"jwt"</c> or <c>"opaque"</c>.</summary>
        public string TokenType { get; }

        private OAuthValidationResult(bool succeeded, string? failureReason, string? subject, string? scope, string? clientId, string tokenType)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
            Subject = subject;
            Scope = scope;
            ClientId = clientId;
            TokenType = tokenType;
        }

        public static OAuthValidationResult Success(string tokenType, string? subject, string? scope, string? clientId = null)
            => new OAuthValidationResult(true, null, subject, scope, clientId, tokenType);

        public static OAuthValidationResult Fail(string tokenType, string reason)
            => new OAuthValidationResult(false, reason, null, null, null, tokenType);
    }
}
