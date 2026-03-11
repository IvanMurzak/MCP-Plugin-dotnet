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
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks.Services
{
    /// <summary>
    /// Wraps an <see cref="IAuthorizationWebhookService"/> with a short-lived in-memory cache
    /// to avoid redundant webhook calls for the same bearer token within the TTL window.
    /// Only successful (allowed) results are cached; denials are never cached so that
    /// a previously-denied client can retry immediately after being granted access.
    /// </summary>
    public class CachedAuthorizationWebhookService : IAuthorizationWebhookService
    {
        readonly IAuthorizationWebhookService _inner;
        readonly TimeSpan _cacheTtl;
        readonly ConcurrentDictionary<string, DateTimeOffset> _cache = new ConcurrentDictionary<string, DateTimeOffset>();

        public CachedAuthorizationWebhookService(IAuthorizationWebhookService inner, TimeSpan cacheTtl)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cacheTtl = cacheTtl;
        }

        public async Task<bool> AuthorizeAiAgentAsync(
            string connectionId,
            string? bearerToken,
            string? remoteIpAddress,
            string? userAgent,
            string? requestPath,
            CancellationToken cancellationToken = default)
        {
            if (TryGetCached(bearerToken))
                return true;

            var result = await _inner.AuthorizeAiAgentAsync(
                connectionId, bearerToken, remoteIpAddress, userAgent, requestPath, cancellationToken);

            if (result)
                SetCached(bearerToken);

            return result;
        }

        public async Task<bool> AuthorizePluginAsync(
            string connectionId,
            string? bearerToken,
            string? clientName,
            string? clientVersion,
            CancellationToken cancellationToken = default)
        {
            if (TryGetCached(bearerToken))
                return true;

            var result = await _inner.AuthorizePluginAsync(
                connectionId, bearerToken, clientName, clientVersion, cancellationToken);

            if (result)
                SetCached(bearerToken);

            return result;
        }

        bool TryGetCached(string? bearerToken)
        {
            if (bearerToken == null)
                return false;

            if (_cache.TryGetValue(bearerToken, out var expiresAt) && DateTimeOffset.UtcNow < expiresAt)
                return true;

            // Expired entry — remove it
            if (expiresAt != default)
                _cache.TryRemove(bearerToken, out _);

            return false;
        }

        void SetCached(string? bearerToken)
        {
            if (bearerToken == null)
                return;

            _cache[bearerToken] = DateTimeOffset.UtcNow + _cacheTtl;
        }
    }
}
