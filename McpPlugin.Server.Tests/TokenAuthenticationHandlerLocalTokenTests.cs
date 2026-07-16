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
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The offline <c>token</c> mode's HTTP-endpoint gate (mcp-authorize g6): the presented Bearer is
    /// compared against the configured secret in constant time; the hub path stays silent (BaseHub does
    /// its own auth); a missing credential yields the non-OAuth <c>Bearer realm</c> 401 challenge.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class TokenAuthenticationHandlerLocalTokenTests
    {
        const string Secret = "the-shared-local-secret";

        static (TokenAuthenticationHandler Handler, DefaultHttpContext Context, AuthenticationScheme Scheme) Create(
            string? configuredSecret)
        {
            var options = new TokenAuthenticationOptions { LocalTokenMode = true, LocalToken = configuredSecret };
            var monitor = new Mock<IOptionsMonitor<TokenAuthenticationOptions>>();
            monitor.Setup(x => x.CurrentValue).Returns(options);
            monitor.Setup(x => x.Get(TokenAuthenticationHandler.SchemeName)).Returns(options);

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var handler = new TokenAuthenticationHandler(
                monitor.Object,
                loggerFactory.Object,
                System.Text.Encodings.Web.UrlEncoder.Default,
                new NoOpAuthorizationWebhookService());

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var scheme = new AuthenticationScheme(
                TokenAuthenticationHandler.SchemeName, TokenAuthenticationHandler.SchemeName, typeof(TokenAuthenticationHandler));
            return (handler, context, scheme);
        }

        [Fact]
        public async Task ValidToken_OnMcpPath_Succeeds_WithTokenClaim()
        {
            var (handler, context, scheme) = Create(Secret);
            context.Request.Headers["Authorization"] = $"Bearer {Secret}";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeTrue();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, Secret).ShouldBeTrue();
        }

        [Fact]
        public async Task InvalidToken_OnMcpPath_Fails()
        {
            var (handler, context, scheme) = Create(Secret);
            context.Request.Headers["Authorization"] = "Bearer wrong-secret";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeFalse();
            result.None.ShouldBeFalse();
        }

        [Fact]
        public async Task InvalidToken_OnHubPath_ReturnsNoResult()
        {
            var (handler, context, scheme) = Create(Secret);
            context.Request.Headers["Authorization"] = "Bearer wrong-secret";
            context.Request.Path = new PathString(Consts.Hub.RemoteApp);
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task NoToken_OnMcpPath_ReturnsNoResult()
        {
            // NoResult lets the RequireAuthorization pipeline issue the 401 challenge.
            var (handler, context, scheme) = Create(Secret);
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task EmptyConfiguredSecret_RejectsAnyPresentedToken()
        {
            // Defensive: the strategy's Validate forbids an empty secret at boot, but the handler must
            // still fail closed if one ever reaches it — an empty expected never matches.
            var (handler, context, scheme) = Create(configuredSecret: "");
            context.Request.Headers["Authorization"] = "Bearer anything";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeFalse();
        }

        [Fact]
        public async Task PrefixSharingToken_Fails_ConstantTimeCompare()
        {
            // A token that shares a long prefix with the secret must still be rejected — proves the
            // compare is over the full fixed-length digest, not an early-exit prefix match.
            var (handler, context, scheme) = Create(Secret);
            context.Request.Headers["Authorization"] = $"Bearer {Secret}-extra";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeFalse();
        }

        [Fact]
        public async Task Challenge_Emits401_WithBearerRealm()
        {
            var (handler, context, scheme) = Create(Secret);
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            await handler.ChallengeAsync(null);

            context.Response.StatusCode.ShouldBe(401);
            var wwwAuth = context.Response.Headers["WWW-Authenticate"].ToString();
            wwwAuth.ShouldContain("Bearer realm=\"MCP Plugin Server\"");
            // Token mode advertises NO resource_metadata (there is no authorization server to discover).
            wwwAuth.ShouldNotContain("resource_metadata");
        }
    }
}
