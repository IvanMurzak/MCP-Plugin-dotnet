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
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    public class UrlNormalizationTests
    {
        [Theory]
        [InlineData("localhost", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.0.0.5", true)]
        [InlineData("::1", true)]
        [InlineData("[::1]", true)]
        [InlineData("example.com", false)]
        [InlineData("10.0.0.1", false)]
        [InlineData("", false)]
        public void IsLoopbackHost(string host, bool expected)
            => UrlNormalization.IsLoopbackHost(host).ShouldBe(expected);

        [Theory]
        [InlineData("http://localhost:23471", "http://127.0.0.1:23471", true)]   // loopback alias
        [InlineData("http://localhost:23471", "http://[::1]:23471", true)]       // ipv6 loopback alias
        [InlineData("http://localhost:23471", "http://localhost:9999", false)]   // port differs
        [InlineData("https://ai-game.dev/mcp", "https://ai-game.dev/mcp", true)] // path preserved
        [InlineData("https://ai-game.dev/mcp", "https://ai-game.dev/other", false)]
        [InlineData("https://ai-game.dev", "https://ai-game.dev:443", true)]     // default port
        [InlineData("https://ai-game.dev/mcp/", "https://ai-game.dev/mcp", true)] // trailing slash
        public void ResourcesMatch(string a, string b, bool expected)
            => UrlNormalization.ResourcesMatch(a, b).ShouldBe(expected);

        [Fact]
        public void NormalizeOrigin_StripsPath_AndAliasesLoopback()
        {
            UrlNormalization.NormalizeOrigin("http://127.0.0.1:5000")
                .ShouldBe("http://localhost:5000");
            UrlNormalization.NormalizeOrigin("https://App.Example.COM")
                .ShouldBe("https://app.example.com:443");
        }

        [Fact]
        public void NormalizeOrigin_Malformed_ReturnsNull()
            => UrlNormalization.NormalizeOrigin("not a url").ShouldBeNull();
    }
}
