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
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Tests for token-based routing in ClientUtils.
    /// Uses unique tokens/connectionIds per test to avoid static state interference.
    /// </summary>
    public class ClientUtilsTokenRoutingTests
    {
        readonly ILogger _logger = new Mock<ILogger>().Object;

        static string UniqueId() => Guid.NewGuid().ToString("N");

        [Fact]
        public void AddClient_WithToken_CreatesTokenMapping()
        {
            // Arrange
            var token = UniqueId();
            var connectionId = UniqueId();

            // Act
            ClientUtils.AddClient<McpServerHub>(connectionId, _logger, token);

            // Assert
            ClientUtils.GetConnectionIdByToken(token).Should().Be(connectionId);
            ClientUtils.GetTokenByConnectionId(connectionId).Should().Be(token);

            // Cleanup
            ClientUtils.RemoveClient<McpServerHub>(connectionId, _logger);
        }

        [Fact]
        public void AddClient_WithoutToken_NoTokenMapping()
        {
            // Arrange
            var connectionId = UniqueId();

            // Act
            ClientUtils.AddClient<McpServerHub>(connectionId, _logger);

            // Assert
            ClientUtils.GetTokenByConnectionId(connectionId).Should().BeNull();

            // Cleanup
            ClientUtils.RemoveClient<McpServerHub>(connectionId, _logger);
        }

        [Fact]
        public void RemoveClient_CleansUpTokenMapping()
        {
            // Arrange
            var token = UniqueId();
            var connectionId = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connectionId, _logger, token);

            // Act
            ClientUtils.RemoveClient<McpServerHub>(connectionId, _logger);

            // Assert
            ClientUtils.GetConnectionIdByToken(token).Should().BeNull();
            ClientUtils.GetTokenByConnectionId(connectionId).Should().BeNull();
        }

        [Fact]
        public void GetConnectionIdByToken_NullToken_ReturnsNull()
        {
            ClientUtils.GetConnectionIdByToken(null).Should().BeNull();
        }

        [Fact]
        public void GetConnectionIdByToken_EmptyToken_ReturnsNull()
        {
            ClientUtils.GetConnectionIdByToken("").Should().BeNull();
        }

        [Fact]
        public void GetTokenByConnectionId_NullConnectionId_ReturnsNull()
        {
            ClientUtils.GetTokenByConnectionId(null).Should().BeNull();
        }

        [Fact]
        public void GetTokenByConnectionId_EmptyConnectionId_ReturnsNull()
        {
            ClientUtils.GetTokenByConnectionId("").Should().BeNull();
        }

        [Fact]
        public void GetConnectionIdByToken_UnknownToken_ReturnsNull()
        {
            ClientUtils.GetConnectionIdByToken(UniqueId()).Should().BeNull();
        }

        [Fact]
        public void MultiplePlugins_IndependentTokenRouting()
        {
            // Arrange
            var tokenA = UniqueId();
            var tokenB = UniqueId();
            var connA = UniqueId();
            var connB = UniqueId();

            // Act
            ClientUtils.AddClient<McpServerHub>(connA, _logger, tokenA);
            ClientUtils.AddClient<McpServerHub>(connB, _logger, tokenB);

            // Assert
            ClientUtils.GetConnectionIdByToken(tokenA).Should().Be(connA);
            ClientUtils.GetConnectionIdByToken(tokenB).Should().Be(connB);
            ClientUtils.GetTokenByConnectionId(connA).Should().Be(tokenA);
            ClientUtils.GetTokenByConnectionId(connB).Should().Be(tokenB);

            // Cleanup
            ClientUtils.RemoveClient<McpServerHub>(connA, _logger);
            ClientUtils.RemoveClient<McpServerHub>(connB, _logger);
        }

        [Fact]
        public void RemoveOnePlugin_DoesNotAffectOther()
        {
            // Arrange
            var tokenA = UniqueId();
            var tokenB = UniqueId();
            var connA = UniqueId();
            var connB = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connA, _logger, tokenA);
            ClientUtils.AddClient<McpServerHub>(connB, _logger, tokenB);

            // Act
            ClientUtils.RemoveClient<McpServerHub>(connA, _logger);

            // Assert
            ClientUtils.GetConnectionIdByToken(tokenA).Should().BeNull();
            ClientUtils.GetConnectionIdByToken(tokenB).Should().Be(connB);

            // Cleanup
            ClientUtils.RemoveClient<McpServerHub>(connB, _logger);
        }
    }
}
