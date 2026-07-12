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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Resolves an authorization-server signing key (by <c>kid</c>) to an <see cref="ECDsa"/>
    /// public key for ES256 verification, backed by a disk-cached JWKS with offline grace and
    /// rate-limited unknown-<c>kid</c> refetch (mcp-authorize b2).
    /// </summary>
    public interface IJwksKeyProvider
    {
        /// <summary>
        /// Return a fresh <see cref="ECDsa"/> for the given <c>kid</c>, or <c>null</c> when the key
        /// cannot be resolved (unknown kid after a rate-limited refetch, or no keys available while
        /// offline with no cache). The caller owns and disposes the returned instance.
        /// </summary>
        Task<ECDsa?> GetSigningKeyAsync(string kid, CancellationToken cancellationToken);
    }

    /// <summary>Fetches the raw JWKS JSON document from the authorization server's <c>jwks_uri</c>.</summary>
    public delegate Task<string?> JwksFetch(CancellationToken cancellationToken);
}
