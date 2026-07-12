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
    /// Validates a presented bearer token in OAuth resource-server mode (mcp-authorize b2): ES256
    /// JWTs against the AS JWKS with strict <c>iss</c>/<c>aud</c>/<c>exp</c>, opaque tokens via
    /// introspection. Always fails closed.
    /// </summary>
    public interface IOAuthTokenValidator
    {
        Task<OAuthValidationResult> ValidateAsync(string token, CancellationToken cancellationToken);
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
