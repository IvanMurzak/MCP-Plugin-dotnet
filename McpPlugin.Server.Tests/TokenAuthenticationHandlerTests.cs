/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    [Collection("McpPlugin.Server")]
    public class TokenAuthenticationHandlerTests
    {
        static string UniqueId() => Guid.NewGuid().ToString("N");

        async Task<AuthenticateResult> AuthenticateAsync(
            string? authorizationHeader,
            bool requireToken = true,
            string? serverToken = null,
            string? registeredToken = null,
            string? registeredConnectionId = null)
        {
            // Register token mapping if provided
            if (registeredToken != null && registeredConnectionId != null)
            {
                ClientUtils.AddClient<McpServerHub>(registeredConnectionId, null, registeredToken);
            }

            try
            {
                var options = new TokenAuthenticationOptions
                {
                    RequireToken = requireToken,
                    ServerToken = serverToken
                };

                var optionsMonitor = new Mock<IOptionsMonitor<TokenAuthenticationOptions>>();
                optionsMonitor.Setup(x => x.CurrentValue).Returns(options);
                optionsMonitor.Setup(x => x.Get(TokenAuthenticationHandler.SchemeName)).Returns(options);

                var loggerFactory = new Mock<ILoggerFactory>();
                loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                    .Returns(new Mock<ILogger>().Object);

                var handler = new TokenAuthenticationHandler(
                    optionsMonitor.Object,
                    loggerFactory.Object,
                    System.Text.Encodings.Web.UrlEncoder.Default,
                    new NoOpAuthorizationWebhookService());

                var context = new DefaultHttpContext();
                if (authorizationHeader != null)
                    context.Request.Headers["Authorization"] = authorizationHeader;

                var scheme = new AuthenticationScheme(
                    TokenAuthenticationHandler.SchemeName,
                    TokenAuthenticationHandler.SchemeName,
                    typeof(TokenAuthenticationHandler));

                await handler.InitializeAsync(scheme, context);
                return await handler.AuthenticateAsync();
            }
            finally
            {
                // Cleanup token mapping
                if (registeredConnectionId != null)
                    ClientUtils.RemoveClient<McpServerHub>(registeredConnectionId, null);
            }
        }

        [Fact]
        public async Task RequireTokenFalse_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(null, requireToken: false);
            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task NoAuthHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(null, requireToken: true);
            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task NonBearerHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync("Basic abc123", requireToken: true);
            result.None.ShouldBeTrue();
        }

        [Fact]
        public async Task EmptyBearerToken_ReturnsFail()
        {
            var result = await AuthenticateAsync("Bearer ", requireToken: true);
            result.Succeeded.ShouldBeFalse();
            result.Failure!.Message.ShouldBe("Empty Bearer token.");
        }

        [Fact]
        public async Task ValidRegisteredToken_ReturnsSuccess()
        {
            var token = UniqueId();
            var connId = UniqueId();

            var result = await AuthenticateAsync(
                $"Bearer {token}",
                requireToken: true,
                registeredToken: token,
                registeredConnectionId: connId);

            result.Succeeded.ShouldBeTrue();
            result.Principal.ShouldNotBeNull();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, token).ShouldBeTrue();
            result.Principal.HasClaim("connection_id", connId).ShouldBeTrue();
        }

        [Fact]
        public async Task ValidServerToken_ReturnsSuccess()
        {
            var serverToken = UniqueId();

            var result = await AuthenticateAsync(
                $"Bearer {serverToken}",
                requireToken: true,
                serverToken: serverToken);

            result.Succeeded.ShouldBeTrue();
            result.Principal.ShouldNotBeNull();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, serverToken).ShouldBeTrue();
        }

        [Fact]
        public async Task UnrecognizedToken_ReturnsFail()
        {
            var result = await AuthenticateAsync(
                $"Bearer {UniqueId()}",
                requireToken: true,
                serverToken: "different-token");

            result.Succeeded.ShouldBeFalse();
            result.Failure!.Message.ShouldBe("Invalid or unrecognized token.");
        }

        [Fact]
        public async Task RegisteredTokenPrioritizedOverServerToken()
        {
            // When a token matches both a registered plugin AND the server token,
            // the registered plugin path should be taken (has connection_id claim)
            var token = UniqueId();
            var connId = UniqueId();

            var result = await AuthenticateAsync(
                $"Bearer {token}",
                requireToken: true,
                serverToken: token,
                registeredToken: token,
                registeredConnectionId: connId);

            result.Succeeded.ShouldBeTrue();
            result.Principal!.HasClaim("connection_id", connId).ShouldBeTrue();
        }

        async Task<AuthenticateResult> AuthenticateWithDcrTokenAsync(
            string accessToken,
            bool requireToken = true,
            string? serverToken = null)
        {
            var options = new TokenAuthenticationOptions
            {
                RequireToken = requireToken,
                ServerToken = serverToken
            };

            var optionsMonitor = new Mock<IOptionsMonitor<TokenAuthenticationOptions>>();
            optionsMonitor.Setup(x => x.CurrentValue).Returns(options);
            optionsMonitor.Setup(x => x.Get(TokenAuthenticationHandler.SchemeName)).Returns(options);

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(new Mock<ILogger>().Object);

            var handler = new TokenAuthenticationHandler(
                optionsMonitor.Object,
                loggerFactory.Object,
                System.Text.Encodings.Web.UrlEncoder.Default,
                new NoOpAuthorizationWebhookService());

            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Bearer {accessToken}";

            var scheme = new AuthenticationScheme(
                TokenAuthenticationHandler.SchemeName,
                TokenAuthenticationHandler.SchemeName,
                typeof(TokenAuthenticationHandler));

            await handler.InitializeAsync(scheme, context);
            return await handler.AuthenticateAsync();
        }

        [Fact]
        public async Task DcrAccessToken_ValidToken_ReturnsSuccess()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("DcrValid_" + UniqueId());
            var token = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);

            // Act
            var result = await AuthenticateWithDcrTokenAsync(token!);

            // Assert
            result.Succeeded.ShouldBeTrue();
            result.Principal.ShouldNotBeNull();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, token!).ShouldBeTrue();
            result.Principal.HasClaim("client_id", client.ClientId).ShouldBeTrue();
        }

        [Fact]
        public async Task DcrAccessToken_UnknownToken_ReturnsFail()
        {
            // Arrange
            var randomToken = Guid.NewGuid().ToString();

            // Act
            var result = await AuthenticateWithDcrTokenAsync(randomToken);

            // Assert
            result.Succeeded.ShouldBeFalse();
            result.Failure!.Message.ShouldBe("Invalid or unrecognized token.");
        }

        [Fact]
        public async Task DcrAccessToken_TakesPriorityOverServerToken()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("DcrPriority_" + UniqueId());
            var dcrToken = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);
            var differentServerToken = UniqueId();

            // Act
            var result = await AuthenticateWithDcrTokenAsync(dcrToken!, serverToken: differentServerToken);

            // Assert
            result.Succeeded.ShouldBeTrue();
            result.Principal.ShouldNotBeNull();
            result.Principal!.HasClaim("client_id", client.ClientId).ShouldBeTrue();
            result.Principal.Claims.ShouldNotContain(c => c.Type == "connection_id");
        }

        (TokenAuthenticationHandler Handler, DefaultHttpContext Context, AuthenticationScheme Scheme) CreateHandlerContext(
            bool requireToken = true,
            string? serverToken = null)
        {
            var options = new TokenAuthenticationOptions
            {
                RequireToken = requireToken,
                ServerToken = serverToken
            };

            var optionsMonitor = new Mock<IOptionsMonitor<TokenAuthenticationOptions>>();
            optionsMonitor.Setup(x => x.CurrentValue).Returns(options);
            optionsMonitor.Setup(x => x.Get(TokenAuthenticationHandler.SchemeName)).Returns(options);

            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(new Mock<ILogger>().Object);

            var handler = new TokenAuthenticationHandler(
                optionsMonitor.Object,
                loggerFactory.Object,
                System.Text.Encodings.Web.UrlEncoder.Default,
                new NoOpAuthorizationWebhookService());

            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();

            var scheme = new AuthenticationScheme(
                TokenAuthenticationHandler.SchemeName,
                TokenAuthenticationHandler.SchemeName,
                typeof(TokenAuthenticationHandler));

            return (handler, context, scheme);
        }

        [Fact]
        public async Task Challenge_Returns401_WithWwwAuthenticateHeader_AndJsonErrorBody()
        {
            // Arrange
            var (handler, context, scheme) = CreateHandlerContext(requireToken: true);
            await handler.InitializeAsync(scheme, context);

            // Act
            await handler.ChallengeAsync(null);

            // Assert
            context.Response.StatusCode.ShouldBe(401);
            context.Response.ContentType!.ShouldContain("application/json");
            context.Response.Headers["WWW-Authenticate"].ToString().ShouldContain("Bearer realm=");

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            body.ShouldContain("\"error\"");
            body.ShouldContain("\"Unauthorized\"");
            body.ShouldContain("\"message\"");
        }

        [Fact]
        public async Task Forbidden_Returns403_WithJsonErrorBody()
        {
            // Arrange
            var (handler, context, scheme) = CreateHandlerContext(requireToken: true);
            await handler.InitializeAsync(scheme, context);

            // Act
            await handler.ForbidAsync(null);

            // Assert
            context.Response.StatusCode.ShouldBe(403);
            context.Response.ContentType!.ShouldContain("application/json");

            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            body.ShouldContain("\"error\"");
            body.ShouldContain("\"Forbidden\"");
            body.ShouldContain("\"message\"");
        }
    }
}
