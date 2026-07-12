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
using System.IO;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Security
{
    /// <summary>
    /// Origin validation returns 403 for a present-but-non-allowed Origin on the MCP endpoint AND
    /// the SignalR negotiate path, and passes when the Origin is absent or allowed — in the allow
    /// sets used by BOTH none and oauth modes (mcp-authorize b2 DoD).
    /// </summary>
    public class OriginValidationMiddlewareTests
    {
        // none mode: no configured public-url → only loopback is allowed.
        static OriginValidationOptions NoneMode() => new OriginValidationOptions(System.Array.Empty<string>(), allowLoopback: true);
        // oauth mode: the RS public-url origin is additionally allowed.
        static OriginValidationOptions OAuthMode() => new OriginValidationOptions(new[] { "https://app.example:443" }, allowLoopback: true);

        static async Task<(int status, bool nextCalled)> Run(OriginValidationOptions options, string path, string? origin)
        {
            var nextCalled = false;
            var middleware = new OriginValidationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, options);

            var ctx = new DefaultHttpContext();
            ctx.Request.Path = new PathString(path);
            ctx.Response.Body = new MemoryStream();
            if (origin != null)
                ctx.Request.Headers["Origin"] = origin;

            await middleware.InvokeAsync(ctx);
            return (ctx.Response.StatusCode, nextCalled);
        }

        [Theory]
        [InlineData("/mcp")]
        [InlineData("/hub/mcp-server/negotiate")]
        public async Task NonAllowedOrigin_Returns403_InBothModes(string path)
        {
            var none = await Run(NoneMode(), path, "https://evil.example");
            none.status.ShouldBe(403);
            none.nextCalled.ShouldBeFalse();

            var oauth = await Run(OAuthMode(), path, "https://evil.example");
            oauth.status.ShouldBe(403);
            oauth.nextCalled.ShouldBeFalse();
        }

        [Theory]
        [InlineData("/mcp")]
        [InlineData("/hub/mcp-server/negotiate")]
        public async Task AbsentOrigin_Passes_InBothModes(string path)
        {
            (await Run(NoneMode(), path, null)).nextCalled.ShouldBeTrue();
            (await Run(OAuthMode(), path, null)).nextCalled.ShouldBeTrue();
        }

        [Theory]
        [InlineData("/mcp")]
        [InlineData("/hub/mcp-server/negotiate")]
        public async Task LoopbackOrigin_Passes_InBothModes(string path)
        {
            (await Run(NoneMode(), path, "http://localhost:6123")).nextCalled.ShouldBeTrue();
            (await Run(OAuthMode(), path, "http://127.0.0.1:6123")).nextCalled.ShouldBeTrue();
        }

        [Fact]
        public async Task PublicUrlOrigin_Allowed_OnlyInOAuthMode()
        {
            (await Run(OAuthMode(), "/mcp", "https://app.example")).nextCalled.ShouldBeTrue();

            var none = await Run(NoneMode(), "/mcp", "https://app.example");
            none.status.ShouldBe(403);
            none.nextCalled.ShouldBeFalse();
        }

        [Fact]
        public async Task NonGuardedPath_WithHostileOrigin_Passes()
        {
            // Discovery/help endpoints are not guarded.
            var result = await Run(OAuthMode(), "/.well-known/oauth-protected-resource", "https://evil.example");
            result.nextCalled.ShouldBeTrue();
        }
    }
}
