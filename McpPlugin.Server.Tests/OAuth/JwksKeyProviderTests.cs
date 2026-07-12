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
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    /// <summary>
    /// JWKS key-provider behavior (mcp-authorize b2): disk cache, offline grace, 24 h refresh, and
    /// rate-limited unknown-<c>kid</c> refetch.
    /// </summary>
    public class JwksKeyProviderTests
    {
        const string Kid1 = "k1";
        const string Kid2 = "k2";
        static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        sealed class CountingFetch
        {
            public string? Current;
            public bool Fail;
            public int Calls;

            public JwksFetch Delegate => _ =>
            {
                Calls++;
                if (Fail)
                    throw new HttpRequestException("offline");
                return Task.FromResult(Current);
            };
        }

        static async Task<bool> Resolves(IJwksKeyProvider provider, string kid)
        {
            var key = await provider.GetSigningKeyAsync(kid, CancellationToken.None);
            using (key)
                return key != null;
        }

        [Fact]
        public async Task FetchSuccess_ResolvesKey_AndWritesDiskCache()
        {
            using var key = TestJwt.CreateKey();
            var fetch = new CountingFetch { Current = TestJwt.BuildJwks(key, Kid1) };
            var cache = new InMemoryJwksDiskCache();
            var provider = new JwksKeyProvider(fetch.Delegate, cache, () => Start);

            (await Resolves(provider, Kid1)).ShouldBeTrue();
            cache.Writes.ShouldBe(1);
            cache.Value.ShouldNotBeNull();
        }

        [Fact]
        public async Task OfflineWithCache_ResolvesFromDisk()
        {
            using var key = TestJwt.CreateKey();
            var cache = new InMemoryJwksDiskCache { Value = TestJwt.BuildJwks(key, Kid1) };
            var fetch = new CountingFetch { Fail = true };
            var provider = new JwksKeyProvider(fetch.Delegate, cache, () => Start);

            (await Resolves(provider, Kid1)).ShouldBeTrue();
            fetch.Calls.ShouldBe(1); // attempted the network, then fell back to disk
        }

        [Fact]
        public async Task OfflineWithoutCache_ReturnsNull()
        {
            var cache = new InMemoryJwksDiskCache();
            var fetch = new CountingFetch { Fail = true };
            var provider = new JwksKeyProvider(fetch.Delegate, cache, () => Start);

            (await Resolves(provider, Kid1)).ShouldBeFalse();
        }

        [Fact]
        public async Task UnknownKid_RefetchIsRateLimited()
        {
            using var key = TestJwt.CreateKey();
            var now = Start;
            var fetch = new CountingFetch { Current = TestJwt.BuildJwks(key, Kid1) };
            var provider = new JwksKeyProvider(
                fetch.Delegate, new InMemoryJwksDiskCache(), () => now,
                unknownKidMinRefetchInterval: TimeSpan.FromSeconds(60));

            (await Resolves(provider, Kid1)).ShouldBeTrue();
            fetch.Calls.ShouldBe(1);

            // Unknown kid within the rate-limit window → NO refetch.
            (await Resolves(provider, Kid2)).ShouldBeFalse();
            fetch.Calls.ShouldBe(1);

            // After the window, an unknown kid triggers exactly one refetch — and now the AS has k2.
            now = Start.AddSeconds(61);
            using var key2 = TestJwt.CreateKey();
            fetch.Current = BuildTwoKeyJwks(key, Kid1, key2, Kid2);
            (await Resolves(provider, Kid2)).ShouldBeTrue();
            fetch.Calls.ShouldBe(2);
        }

        [Fact]
        public async Task StaleSet_RefreshedAfterInterval()
        {
            using var key = TestJwt.CreateKey();
            var now = Start;
            var fetch = new CountingFetch { Current = TestJwt.BuildJwks(key, Kid1) };
            var provider = new JwksKeyProvider(
                fetch.Delegate, new InMemoryJwksDiskCache(), () => now,
                refreshInterval: TimeSpan.FromHours(24));

            (await Resolves(provider, Kid1)).ShouldBeTrue();
            fetch.Calls.ShouldBe(1);

            // Within the window: served from memory, no refetch.
            (await Resolves(provider, Kid1)).ShouldBeTrue();
            fetch.Calls.ShouldBe(1);

            // After 24 h: the set is refreshed.
            now = Start.AddHours(25);
            (await Resolves(provider, Kid1)).ShouldBeTrue();
            fetch.Calls.ShouldBe(2);
        }

        static string BuildTwoKeyJwks(ECDsa a, string kidA, ECDsa b, string kidB)
        {
            // Merge two single-key JWKS documents into one { "keys": [ ... ] }.
            var ja = TestJwt.BuildJwks(a, kidA);
            var jb = TestJwt.BuildJwks(b, kidB);
            var innerA = ja.Substring(ja.IndexOf('[') + 1, ja.LastIndexOf(']') - ja.IndexOf('[') - 1);
            var innerB = jb.Substring(jb.IndexOf('[') + 1, jb.LastIndexOf(']') - jb.IndexOf('[') - 1);
            return "{\"keys\":[" + innerA + "," + innerB + "]}";
        }
    }
}
