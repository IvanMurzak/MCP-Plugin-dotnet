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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Security
{
    public class OriginPolicyTests
    {
        static readonly string Hub = Consts.Hub.RemoteApp;

        [Theory]
        [InlineData("/", true)]
        [InlineData("/mcp", true)]
        [InlineData("/mcp/p/3fa9c1e2", true)]
        [InlineData("/hub/mcp-server", true)]
        [InlineData("/hub/mcp-server/negotiate", true)]
        [InlineData("/help", false)]
        [InlineData("/.well-known/oauth-protected-resource", false)]
        [InlineData("/oauth/token", false)]
        [InlineData("/api/tools", false)]
        public void IsGuardedPath(string path, bool expected)
            => OriginPolicy.IsGuardedPath(new PathString(path), Hub).ShouldBe(expected);

        static OriginValidationOptions Options()
            => new OriginValidationOptions(new[] { "https://app.example:443" }, allowLoopback: true);

        [Theory]
        [InlineData(null, true)]                       // absent → allowed (native client)
        [InlineData("", true)]                         // absent → allowed
        [InlineData("http://localhost:5000", true)]    // loopback → allowed
        [InlineData("http://127.0.0.1:9", true)]       // loopback → allowed
        [InlineData("https://app.example", true)]      // explicitly allowed origin
        [InlineData("https://evil.example", false)]    // not allowed → reject
        [InlineData("garbage", false)]                 // malformed → reject
        public void IsOriginAllowed(string? origin, bool expected)
            => OriginPolicy.IsOriginAllowed(origin, Options()).ShouldBe(expected);

        [Fact]
        public void LoopbackDisallowed_WhenAllowLoopbackFalse()
        {
            var options = new OriginValidationOptions(System.Array.Empty<string>(), allowLoopback: false);
            OriginPolicy.IsOriginAllowed("http://localhost:5000", options).ShouldBeFalse();
        }
    }
}
