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
using com.IvanMurzak.McpPlugin.Server.Auth;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class ClientRegistrationStoreTests
    {
        static string UniqueId() => Guid.NewGuid().ToString("N");

        [Fact]
        public void Register_ReturnsClientWithGeneratedCredentials()
        {
            // Arrange
            var clientName = "TestClient_" + UniqueId();

            // Act
            var client = ClientRegistrationStore.Register(clientName);

            // Assert
            client.ClientId.Should().NotBeNullOrEmpty();
            client.ClientSecret.Should().NotBeNullOrEmpty();
            client.ClientName.Should().Be(clientName);
            client.IssuedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Register_WithNullName_SetsNullClientName()
        {
            // Act
            var client = ClientRegistrationStore.Register(null);

            // Assert
            client.ClientName.Should().BeNull();
            client.ClientId.Should().NotBeNullOrEmpty();
            client.ClientSecret.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Register_TwoCalls_ReturnDifferentCredentials()
        {
            // Act
            var clientA = ClientRegistrationStore.Register("ClientA_" + UniqueId());
            var clientB = ClientRegistrationStore.Register("ClientB_" + UniqueId());

            // Assert
            clientA.ClientId.Should().NotBe(clientB.ClientId);
            clientA.ClientSecret.Should().NotBe(clientB.ClientSecret);
        }

        [Fact]
        public void IssueAccessToken_WithValidCredentials_ReturnsToken()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("TokenClient_" + UniqueId());

            // Act
            var token = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);

            // Assert
            token.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void IssueAccessToken_WithWrongSecret_ReturnsNull()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("WrongSecretClient_" + UniqueId());

            // Act
            var token = ClientRegistrationStore.IssueAccessToken(client.ClientId, "wrong-secret");

            // Assert
            token.Should().BeNull();
        }

        [Fact]
        public void IssueAccessToken_WithUnknownClientId_ReturnsNull()
        {
            // Act
            var token = ClientRegistrationStore.IssueAccessToken("nonexistent-id", "any-secret");

            // Assert
            token.Should().BeNull();
        }

        [Fact]
        public void IssueAccessToken_TwoCalls_ReturnDifferentTokens()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("TwoTokensClient_" + UniqueId());

            // Act
            var token1 = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);
            var token2 = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);

            // Assert
            token1.Should().NotBeNullOrEmpty();
            token2.Should().NotBeNullOrEmpty();
            token1.Should().NotBe(token2);
        }

        [Fact]
        public void TryGetClientIdByAccessToken_WithValidToken_ReturnsClientId()
        {
            // Arrange
            var client = ClientRegistrationStore.Register("LookupClient_" + UniqueId());
            var token = ClientRegistrationStore.IssueAccessToken(client.ClientId, client.ClientSecret);

            // Act
            var resolvedClientId = ClientRegistrationStore.TryGetClientIdByAccessToken(token!);

            // Assert
            resolvedClientId.Should().Be(client.ClientId);
        }

        [Fact]
        public void TryGetClientIdByAccessToken_WithUnknownToken_ReturnsNull()
        {
            // Act
            var resolvedClientId = ClientRegistrationStore.TryGetClientIdByAccessToken("nonexistent-token");

            // Assert
            resolvedClientId.Should().BeNull();
        }

        [Fact]
        public void TryGetClientIdByAccessToken_TokenIsSpecificToClient()
        {
            // Arrange
            var clientA = ClientRegistrationStore.Register("SpecificA_" + UniqueId());
            var clientB = ClientRegistrationStore.Register("SpecificB_" + UniqueId());
            var tokenForA = ClientRegistrationStore.IssueAccessToken(clientA.ClientId, clientA.ClientSecret);

            // Act
            var resolvedClientId = ClientRegistrationStore.TryGetClientIdByAccessToken(tokenForA!);

            // Assert
            resolvedClientId.Should().Be(clientA.ClientId);
            resolvedClientId.Should().NotBe(clientB.ClientId);
        }
    }
}
