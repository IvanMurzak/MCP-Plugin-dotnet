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
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    /// <summary>Introspection caching + fail-closed behavior (mcp-authorize b2).</summary>
    public class IntrospectionClientTests
    {
        static readonly DateTimeOffset Start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        sealed class CountingPost
        {
            public string? Response;
            public bool Throw;
            public int Calls;

            public IntrospectionPost Delegate => (token, ct) =>
            {
                Calls++;
                if (Throw)
                    throw new InvalidOperationException("network");
                return Task.FromResult(Response);
            };
        }

        [Fact]
        public async Task ActiveToken_ParsedAndReturned()
        {
            var post = new CountingPost { Response = "{\"active\":true,\"sub\":\"u1\",\"scope\":\"mcp:agent\"}" };
            var client = new IntrospectionClient(post.Delegate, () => Start);

            var result = await client.IntrospectAsync("agd_pat_x", CancellationToken.None);

            result.Active.ShouldBeTrue();
            result.Subject.ShouldBe("u1");
            result.Scope.ShouldBe("mcp:agent");
        }

        [Fact]
        public async Task InactiveToken_ReturnedInactive()
        {
            var post = new CountingPost { Response = "{\"active\":false}" };
            var client = new IntrospectionClient(post.Delegate, () => Start);

            var result = await client.IntrospectAsync("agd_pat_x", CancellationToken.None);

            result.Active.ShouldBeFalse();
        }

        [Fact]
        public async Task Result_CachedWithinTtl_ThenRefetched()
        {
            var now = Start;
            var post = new CountingPost { Response = "{\"active\":true,\"sub\":\"u1\"}" };
            var client = new IntrospectionClient(post.Delegate, () => now, TimeSpan.FromSeconds(60));

            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeTrue();
            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeTrue();
            post.Calls.ShouldBe(1); // second served from cache

            now = Start.AddSeconds(61);
            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeTrue();
            post.Calls.ShouldBe(2); // cache expired
        }

        [Fact]
        public async Task TransportError_FailsClosed_AndNotCached()
        {
            var post = new CountingPost { Response = null }; // simulates non-2xx / transport error
            var client = new IntrospectionClient(post.Delegate, () => Start);

            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeFalse();
            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeFalse();
            post.Calls.ShouldBe(2); // failures are not cached — each call retries
        }

        [Fact]
        public async Task Exception_FailsClosed()
        {
            var post = new CountingPost { Throw = true };
            var client = new IntrospectionClient(post.Delegate, () => Start);

            var result = await client.IntrospectAsync("t", CancellationToken.None);

            result.Active.ShouldBeFalse();
        }

        [Fact]
        public async Task MalformedResponse_FailsClosed_AndNotCached()
        {
            var post = new CountingPost { Response = "not json" };
            var client = new IntrospectionClient(post.Delegate, () => Start);

            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeFalse();
            post.Calls.ShouldBe(1);
            (await client.IntrospectAsync("t", CancellationToken.None)).Active.ShouldBeFalse();
            post.Calls.ShouldBe(2); // malformed responses are not cached
        }
    }
}
