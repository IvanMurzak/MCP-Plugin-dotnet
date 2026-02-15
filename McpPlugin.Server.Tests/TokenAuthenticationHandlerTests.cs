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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
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
                    System.Text.Encodings.Web.UrlEncoder.Default);

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
            result.None.Should().BeTrue();
        }

        [Fact]
        public async Task NoAuthHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(null, requireToken: true);
            result.None.Should().BeTrue();
        }

        [Fact]
        public async Task NonBearerHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync("Basic abc123", requireToken: true);
            result.None.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyBearerToken_ReturnsFail()
        {
            var result = await AuthenticateAsync("Bearer ", requireToken: true);
            result.Succeeded.Should().BeFalse();
            result.Failure?.Message.Should().Be("Empty Bearer token.");
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

            result.Succeeded.Should().BeTrue();
            result.Principal.Should().NotBeNull();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, token).Should().BeTrue();
            result.Principal.HasClaim("connection_id", connId).Should().BeTrue();
        }

        [Fact]
        public async Task ValidServerToken_ReturnsSuccess()
        {
            var serverToken = UniqueId();

            var result = await AuthenticateAsync(
                $"Bearer {serverToken}",
                requireToken: true,
                serverToken: serverToken);

            result.Succeeded.Should().BeTrue();
            result.Principal.Should().NotBeNull();
            result.Principal!.HasClaim(TokenAuthenticationHandler.TokenClaimType, serverToken).Should().BeTrue();
        }

        [Fact]
        public async Task UnrecognizedToken_ReturnsFail()
        {
            var result = await AuthenticateAsync(
                $"Bearer {UniqueId()}",
                requireToken: true,
                serverToken: "different-token");

            result.Succeeded.Should().BeFalse();
            result.Failure?.Message.Should().Be("Invalid or unrecognized token.");
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

            result.Succeeded.Should().BeTrue();
            result.Principal!.HasClaim("connection_id", connId).Should().BeTrue();
        }
    }
}
