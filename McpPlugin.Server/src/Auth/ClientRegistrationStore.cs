/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// In-memory store for OAuth 2.0 Dynamic Client Registration (RFC 7591).
    /// Holds registered clients and their issued access tokens.
    /// All state is process-lifetime only; clients re-register on server restart.
    /// </summary>
    public static class ClientRegistrationStore
    {
        // ----------------------------------------------------------------------------
        // Note: The _clients and _accessTokens dictionaries grow unbounded
        // in memory without any cleanup mechanism or expiration.
        // In long-running server processes, this could lead to memory exhaustion
        // as clients register repeatedly (especially if there's no rate limiting on registration)
        // ----------------------------------------------------------------------------

        // ----------------------------------------------------------------------------
        // Note: Security concern:
        // Access tokens and client secrets are stored indefinitely in-memory without expiration.
        // The ClientRegistrationStore uses ConcurrentDictionary with no cleanup mechanism,
        // which means tokens remain valid for the entire process lifetime.
        // This creates two issues: (1) compromised tokens cannot be revoked without restarting the server,
        // and (2) memory usage will grow unbounded in long-running servers with many client registrations.
        // Consider implementing token expiration, revocation capabilities,
        // or at least a cleanup mechanism for old/unused tokens.
        // ----------------------------------------------------------------------------

        // ----------------------------------------------------------------------------
        // Note: Each call to IssueAccessToken creates a new access token without invalidating previous ones.
        // A single client can accumulate unlimited active tokens by repeatedly calling the token endpoint,
        // and there's no mechanism to revoke or expire old tokens.
        // This could lead to security issues if a token is compromised, as there's no way to invalidate it.
        // Consider implementing token revocation, expiration, or limiting active tokens per client.
        // ----------------------------------------------------------------------------

        // client_id → RegisteredClient
        private static readonly ConcurrentDictionary<string, RegisteredClient> _clients
            = new ConcurrentDictionary<string, RegisteredClient>();


        // access_token → client_id
        private static readonly ConcurrentDictionary<string, string> _accessTokens
            = new ConcurrentDictionary<string, string>();

        static string GenerateSecureToken(int byteCount = 32)
            => Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();

        /// <summary>
        /// Registers a new client and returns its generated credentials.
        /// Registration is open (no prior auth needed) for deployments with AuthOption.none.
        /// </summary>
        public static RegisteredClient Register(string? clientName)
        {
            var client = new RegisteredClient
            {
                ClientId = GenerateSecureToken(16),
                ClientSecret = GenerateSecureToken(32),
                ClientName = clientName,
                IssuedAt = DateTimeOffset.UtcNow
            };
            _clients[client.ClientId] = client;
            return client;
        }

        /// <summary>
        /// Validates client credentials and, if valid, issues a new unique access token.
        /// Returns null if the client_id is unknown or the secret does not match.
        /// </summary>
        public static string? IssueAccessToken(string clientId, string clientSecret)
        {
            if (!_clients.TryGetValue(clientId, out var client))
                return null;

            if (!string.Equals(client.ClientSecret, clientSecret, StringComparison.Ordinal))
                return null;

            var accessToken = GenerateSecureToken(32);
            _accessTokens[accessToken] = clientId;
            return accessToken;
        }

        /// <summary>
        /// Returns the client_id associated with a previously issued access token,
        /// or null if the token is unrecognised.
        /// </summary>
        public static string? TryGetClientIdByAccessToken(string accessToken)
        {
            _accessTokens.TryGetValue(accessToken, out var clientId);
            return clientId;
        }
    }

    public class RegisteredClient
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
    }
}
