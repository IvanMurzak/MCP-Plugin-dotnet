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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Default <see cref="IJwksKeyProvider"/> (mcp-authorize b2). Behavior:
    /// <list type="bullet">
    ///   <item><b>Disk cache</b> — the fetched JWKS is persisted via <see cref="IJwksDiskCache"/>,
    ///         co-located with the b1 machine store at <c>~/.ai-game-dev/jwks-cache.json</c>.</item>
    ///   <item><b>Offline grace</b> — when the network fetch fails, the last cached JWKS keeps
    ///         validating tokens; validation only fails once there is neither a live nor a cached
    ///         key set.</item>
    ///   <item><b>Refresh</b> — the in-memory set is refreshed after <see cref="_refreshInterval"/>
    ///         (24 h by default).</item>
    ///   <item><b>Unknown-<c>kid</c> refetch</b> — a request for an unseen <c>kid</c> triggers a
    ///         refetch, rate-limited to at most once per <see cref="_unknownKidMinRefetchInterval"/>
    ///         so a bad/spoofed <c>kid</c> cannot be used to hammer the AS.</item>
    /// </list>
    /// A single <see cref="SemaphoreSlim"/> serializes network fetches to avoid a thundering herd.
    /// </summary>
    public sealed class JwksKeyProvider : IJwksKeyProvider
    {
        private readonly JwksFetch _fetch;
        private readonly IJwksDiskCache _cache;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _refreshInterval;
        private readonly TimeSpan _unknownKidMinRefetchInterval;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private JsonWebKeySet? _current;
        private DateTimeOffset _loadedAt;
        private DateTimeOffset _lastRefetchAttempt = DateTimeOffset.MinValue;

        public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromHours(24);
        public static readonly TimeSpan DefaultUnknownKidMinRefetchInterval = TimeSpan.FromSeconds(60);

        public JwksKeyProvider(
            JwksFetch fetch,
            IJwksDiskCache cache,
            Func<DateTimeOffset>? now = null,
            TimeSpan? refreshInterval = null,
            TimeSpan? unknownKidMinRefetchInterval = null,
            ILogger? logger = null)
        {
            _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
            _unknownKidMinRefetchInterval = unknownKidMinRefetchInterval ?? DefaultUnknownKidMinRefetchInterval;
            _logger = logger;
        }

        public async Task<ECDsa?> GetSigningKeyAsync(string kid, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(kid))
                return null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _now();

                // First use, or the in-memory set has aged past the refresh window.
                if (_current == null || now - _loadedAt >= _refreshInterval)
                    await RefreshAsync(now, cancellationToken).ConfigureAwait(false);

                if (_current != null && _current.ContainsKid(kid))
                    return _current.CreateEcdsa(kid);

                // Unknown kid: rate-limited refetch — a rotated signing key that landed before the
                // 24 h refresh should still be picked up, but a bogus kid must not be a DoS lever.
                if (now - _lastRefetchAttempt >= _unknownKidMinRefetchInterval)
                    await RefreshAsync(now, cancellationToken).ConfigureAwait(false);

                return _current != null ? _current.CreateEcdsa(kid) : null;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task RefreshAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            _lastRefetchAttempt = now;

            string? json = null;
            try
            {
                json = await _fetch(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger?.LogWarning(ex, "JWKS fetch failed; falling back to the disk cache (offline grace).");
            }

            if (json != null && JsonWebKeySet.TryParse(json, out var fresh))
            {
                _current = fresh;
                _loadedAt = now;
                try
                {
                    _cache.Write(json);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to persist the JWKS disk cache.");
                }
                return;
            }

            // Network path unavailable/invalid: keep any in-memory set; otherwise load the disk
            // cache so a restarted, currently-offline RS can still validate tokens.
            if (_current == null)
            {
                var cached = SafeReadCache();
                if (cached != null && JsonWebKeySet.TryParse(cached, out var fromDisk))
                {
                    _current = fromDisk;
                    _loadedAt = now;
                    _logger?.LogInformation("Loaded JWKS from the disk cache (offline grace).");
                }
            }
        }

        private string? SafeReadCache()
        {
            try
            {
                return _cache.Read();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read the JWKS disk cache.");
                return null;
            }
        }
    }
}
