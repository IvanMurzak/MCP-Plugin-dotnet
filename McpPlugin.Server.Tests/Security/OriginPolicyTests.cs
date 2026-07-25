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
        // ── The tool-EXECUTING REST surfaces (2026-07-25). `/api/tools` asserted FALSE here until this
        //    change: `OriginValidationMiddleware` never ran on it, so a hostile page could drive the local
        //    server's tool routes from the victim's browser (DNS rebinding). Tightened, never loosened.
        [InlineData("/api/tools", true)]
        [InlineData("/api/tools/ping", true)]
        [InlineData("/api/system-tools", true)]
        [InlineData("/api/system-tools/ping", true)]
        // The pinned family — including the bare nginx-stripped pinned MCP endpoint, whose /mcp/p/{pin}
        // twin was already guarded by the /mcp/ prefix.
        [InlineData("/p/3fa9c1e2", true)]
        [InlineData("/p/3fa9c1e2/api/tools", true)]
        [InlineData("/p/3fa9c1e2/api/tools/ping", true)]
        [InlineData("/p/3fa9c1e2/api/system-tools", true)]
        [InlineData("/p/3fa9c1e2/api/system-tools/ping", true)]
        // Segment-boundary precision: a path that merely SHARES a prefix is not a tool surface.
        [InlineData("/api/toolsets", false)]
        [InlineData("/api/system-toolsets", false)]
        [InlineData("/api", false)]
        [InlineData("/api/other", false)]
        [InlineData("/pricing", false)]
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
