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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
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
    [Collection("McpPlugin.Server")]
    public class TokenAuthenticationHandlerOAuthTests
    {
        sealed class FakeValidator : IOAuthTokenValidator
        {
            private readonly OAuthValidationResult _result;
            public FakeValidator(OAuthValidationResult result) => _result = result;
            public Task<OAuthValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
                => Task.FromResult(_result);
            public Task<OAuthValidationResult> ValidateAsync(string token, TokenValidationPlane plane, CancellationToken cancellationToken)
                => Task.FromResult(_result);
        }

        static readonly OAuthResourceServerConfig Config = new OAuthResourceServerConfig("https://as.example", "http://localhost:23471");

        static (TokenAuthenticationHandler Handler, DefaultHttpContext Context, AuthenticationScheme Scheme) Create(
            IOAuthTokenValidator? validator)
        {
            var options = new TokenAuthenticationOptions { OAuthMode = true };
            var monitor = new Mock<IOptionsMonitor<TokenAuthenticationOptions>>();
            monitor.Setup(x => x.CurrentValue).Returns(options);
            monitor.Setup(x => x.Get(TokenAuthenticationHandler.SchemeName)).Returns(options);

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

            var handler = new TokenAuthenticationHandler(
                monitor.Object,
                loggerFactory.Object,
                System.Text.Encodings.Web.UrlEncoder.Default,
                new NoOpAuthorizationWebhookService(),
                validator,
                Config);

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var scheme = new AuthenticationScheme(
                TokenAuthenticationHandler.SchemeName, TokenAuthenticationHandler.SchemeName, typeof(TokenAuthenticationHandler));
            return (handler, context, scheme);
        }

        [Fact]
        public async Task ValidToken_Succeeds_WithSubjectAndScopeClaims()
        {
            var validator = new FakeValidator(OAuthValidationResult.Success("jwt", "user-9", "mcp:agent", "client-x"));
            var (handler, context, scheme) = Create(validator);
            context.Request.Headers["Authorization"] = "Bearer some.jwt.token";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeTrue();
            result.Principal!.HasClaim(TokenAuthenticationHandler.SubjectClaimType, "user-9").ShouldBeTrue();
            result.Principal.HasClaim(TokenAuthenticationHandler.ScopeClaimType, "mcp:agent").ShouldBeTrue();
            result.Principal.HasClaim(TokenAuthenticationHandler.ClientIdClaimType, "client-x").ShouldBeTrue();
        }

        [Fact]
        public async Task InvalidToken_OnMcpPath_Fails()
        {
            var validator = new FakeValidator(OAuthValidationResult.Fail("jwt", "invalid signature"));
            var (handler, context, scheme) = Create(validator);
            context.Request.Headers["Authorization"] = "Bearer bad.jwt.token";
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.Succeeded.ShouldBeFalse();
            result.None.ShouldBeFalse();
        }

        [Fact]
        public async Task InvalidToken_OnHubPath_ReturnsNoResult()
        {
            var validator = new FakeValidator(OAuthValidationResult.Fail("jwt", "invalid signature"));
            var (handler, context, scheme) = Create(validator);
            context.Request.Headers["Authorization"] = "Bearer bad.jwt.token";
            context.Request.Path = new PathString(Consts.Hub.RemoteApp);
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task NoToken_ReturnsNoResult()
        {
            var (handler, context, scheme) = Create(new FakeValidator(OAuthValidationResult.Success("jwt", "u", "mcp:agent")));
            context.Request.Path = new PathString("/mcp");
            await handler.InitializeAsync(scheme, context);

            var result = await handler.AuthenticateAsync();

            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task Challenge_Emits401_WithAbsoluteResourceMetadata_AndScope()
        {
            var (handler, context, scheme) = Create(null);
            await handler.InitializeAsync(scheme, context);

            await handler.ChallengeAsync(null);

            context.Response.StatusCode.ShouldBe(401);
            var wwwAuth = context.Response.Headers["WWW-Authenticate"].ToString();
            wwwAuth.ShouldContain("Bearer ");
            wwwAuth.ShouldContain($"resource_metadata=\"{Config.ProtectedResourceMetadataUrl()}\"");
            wwwAuth.ShouldContain("scope=\"mcp:agent\"");

            // The resource_metadata URL must be ABSOLUTE (a real MCP client rejects relative URLs).
            Config.ProtectedResourceMetadataUrl().ShouldStartWith("http://localhost:23471/.well-known/oauth-protected-resource");
        }
    }
}
