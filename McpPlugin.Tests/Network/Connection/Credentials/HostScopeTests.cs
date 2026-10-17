/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    public sealed class HostScopeTests
    {
        [Theory]
        [InlineData("http://localhost")]
        [InlineData("http://localhost:8080")]
        [InlineData("https://LOCALHOST:443/hub")]
        [InlineData("http://LocalHost:5000/")]
        [InlineData("http://127.0.0.1")]
        [InlineData("http://127.0.0.1:11111")]
        [InlineData("http://127.255.255.254:80")]
        [InlineData("http://127.1.2.3")]
        [InlineData("http://127.1")] // System.Uri canonicalizes to 127.0.0.1, which is what the client dials
        [InlineData("http://[::1]")]
        [InlineData("http://[::1]:8080")]
        [InlineData("http://[0:0:0:0:0:0:0:1]:8080")]
        [InlineData("http://[::ffff:127.0.0.1]:8080")]
        [InlineData("  http://localhost:8080  ")]
        public void IsLoopback_True(string url) => HostScope.IsLoopback(url).ShouldBeTrue();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("localhost:8080")]
        [InlineData("not a url")]
        [InlineData("https://ai-game.dev")]
        [InlineData("http://evil.com")]
        [InlineData("http://localhost@evil.com")]
        [InlineData("http://localhost:8080@evil.com")]
        [InlineData("http://user:pass@localhost:8080")]
        [InlineData("http://127.0.0.1.evil.com")]
        [InlineData("http://localhost.evil.com")]
        [InlineData("http://evil.localhost")]
        [InlineData("http://localhost.")]
        [InlineData("http://128.0.0.1")]
        [InlineData("http://126.255.255.255")]
        [InlineData("http://0.0.0.0")]
        [InlineData("http://10.0.0.1")]
        [InlineData("http://192.168.1.10:8080")]
        [InlineData("http://[::2]")]
        [InlineData("http://[::]")]
        [InlineData("http://[::ffff:10.0.0.1]")]
        [InlineData("ftp://localhost")]
        [InlineData("file:///C:/localhost")]
        public void IsLoopback_False(string? url) => HostScope.IsLoopback(url).ShouldBeFalse();

        [Fact]
        public async Task Resolve_Loopback_PrefersAccountToken()
        {
            var token = await CustomOAuthCredentialPolicy.ResolveAsync(
                "http://localhost:8080", () => Task.FromResult<string?>("account"), "explicit", "X_MCP_TOKEN");
            token.ShouldBe("account");
        }

        [Fact]
        public async Task Resolve_Loopback_SignedOut_FallsBackToExplicit()
        {
            var token = await CustomOAuthCredentialPolicy.ResolveAsync(
                "http://[::1]:8080", () => Task.FromResult<string?>(null), "explicit", "X_MCP_TOKEN");
            token.ShouldBe("explicit");
        }

        [Fact]
        public async Task Resolve_Loopback_NothingAvailable_IsAnonymous()
        {
            var token = await CustomOAuthCredentialPolicy.ResolveAsync("http://127.0.0.1", null, null, "X_MCP_TOKEN");
            token.ShouldBeNull();
        }

        [Fact]
        public async Task Resolve_Remote_UsesExplicitToken_AndNeverReadsAccount()
        {
            var accountRead = false;
            var token = await CustomOAuthCredentialPolicy.ResolveAsync(
                "https://my-server.example.com",
                () => { accountRead = true; return Task.FromResult<string?>("account"); },
                "explicit",
                "X_MCP_TOKEN");
            token.ShouldBe("explicit");
            accountRead.ShouldBeFalse();
        }

        [Theory]
        [InlineData("http://localhost@evil.com:9000/path?secret=1")]
        [InlineData("http://127.0.0.1.evil.com")]
        [InlineData("https://my-server.example.com")]
        [InlineData(null)]
        public async Task Resolve_Remote_WithoutExplicit_Refuses(string? host)
        {
            var accountRead = false;
            var ex = await Should.ThrowAsync<CredentialScopeException>(() => CustomOAuthCredentialPolicy.ResolveAsync(
                host, () => { accountRead = true; return Task.FromResult<string?>("account"); }, null, "X_MCP_TOKEN"));
            accountRead.ShouldBeFalse();
            ex.Message.ShouldContain("Refusing to send your ai-game.dev account token to ");
            ex.Message.ShouldContain("set X_MCP_TOKEN or use a localhost server");
            ex.Message.ShouldNotContain("secret");
            ex.Message.ShouldNotContain("localhost@");
        }

        [Fact]
        public void RefusalMessage_ShowsHostAndPort()
            => new CredentialScopeException("http://evil.com:9000/x", "X_MCP_TOKEN").Message
                .ShouldBe("Refusing to send your ai-game.dev account token to evil.com:9000; set X_MCP_TOKEN or use a localhost server.");
    }
}
