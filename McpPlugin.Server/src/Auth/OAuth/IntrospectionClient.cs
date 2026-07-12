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
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Default <see cref="IIntrospectionClient"/> (mcp-authorize b2). Caches active/inactive
    /// answers for <see cref="_cacheTtl"/> (60 s) keyed by a SHA-256 of the token (the raw token is
    /// never used as a dictionary key), and fails closed: any transport/HTTP/parse error yields
    /// <see cref="IntrospectionResult.Inactive"/> and is NOT cached, so a transient outage self-heals
    /// on the next request.
    /// </summary>
    public sealed class IntrospectionClient : IIntrospectionClient
    {
        private readonly IntrospectionPost _post;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _cacheTtl;
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>();

        public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromSeconds(60);

        public IntrospectionClient(
            IntrospectionPost post,
            Func<DateTimeOffset>? now = null,
            TimeSpan? cacheTtl = null,
            ILogger? logger = null)
        {
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _cacheTtl = cacheTtl ?? DefaultCacheTtl;
            _logger = logger;
        }

        public async Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(token))
                return IntrospectionResult.Inactive;

            var key = Hash(token);
            var now = _now();

            if (_cache.TryGetValue(key, out var entry) && now - entry.CachedAt < _cacheTtl)
                return entry.Result;

            string? json;
            try
            {
                json = await _post(token, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogWarning(ex, "Token introspection failed; failing closed.");
                return IntrospectionResult.Inactive; // fail closed, not cached
            }

            if (json == null)
                return IntrospectionResult.Inactive; // transport/HTTP error, not cached

            if (!TryParse(json, out var result))
                return IntrospectionResult.Inactive; // malformed response, not cached

            _cache[key] = new CacheEntry(result, now);
            return result;
        }

        private static bool TryParse(string json, out IntrospectionResult result)
        {
            result = IntrospectionResult.Inactive;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return false;

                var active = root.TryGetProperty("active", out var activeProp)
                    && activeProp.ValueKind == JsonValueKind.True;

                if (!active)
                {
                    result = IntrospectionResult.Inactive;
                    return true;
                }

                string? sub = root.TryGetProperty("sub", out var subProp) && subProp.ValueKind == JsonValueKind.String
                    ? subProp.GetString() : null;
                string? scope = root.TryGetProperty("scope", out var scopeProp) && scopeProp.ValueKind == JsonValueKind.String
                    ? scopeProp.GetString() : null;
                DateTimeOffset? exp = root.TryGetProperty("exp", out var expProp) && expProp.ValueKind == JsonValueKind.Number
                    && expProp.TryGetInt64(out var expUnix)
                    ? DateTimeOffset.FromUnixTimeSeconds(expUnix) : (DateTimeOffset?)null;

                result = new IntrospectionResult(true, sub, scope, exp);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string Hash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private readonly struct CacheEntry
        {
            public IntrospectionResult Result { get; }
            public DateTimeOffset CachedAt { get; }
            public CacheEntry(IntrospectionResult result, DateTimeOffset cachedAt)
            {
                Result = result;
                CachedAt = cachedAt;
            }
        }
    }
}
