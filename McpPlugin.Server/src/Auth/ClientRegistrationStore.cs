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

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// In-memory store for OAuth 2.0 Dynamic Client Registration (RFC 7591).
    /// Holds registered clients and their issued access tokens.
    /// All state is process-lifetime only; clients re-register on server restart.
    /// </summary>
    public static class ClientRegistrationStore
    {
        // client_id → RegisteredClient
        private static readonly ConcurrentDictionary<string, RegisteredClient> _clients
            = new ConcurrentDictionary<string, RegisteredClient>();

        // access_token → client_id
        private static readonly ConcurrentDictionary<string, string> _accessTokens
            = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Registers a new client and returns its generated credentials.
        /// Registration is open (no prior auth needed) for local deployments.
        /// </summary>
        public static RegisteredClient Register(string? clientName)
        {
            var client = new RegisteredClient
            {
                ClientId     = Guid.NewGuid().ToString("N"),
                ClientSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ClientName   = clientName,
                IssuedAt     = DateTimeOffset.UtcNow
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

            var accessToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
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
        public string ClientId     { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string? ClientName  { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
    }
}
