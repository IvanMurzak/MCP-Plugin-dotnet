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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class RequiredAuthMcpStrategyTests
    {
        private readonly RequiredAuthMcpStrategy _strategy = new();

        [Fact]
        public void AuthOption_ReturnsRequired()
        {
            _strategy.AuthOption.Should().Be(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsTrue()
        {
            _strategy.AllowMultipleConnections.Should().BeTrue();
        }

        [Fact]
        public void Validate_WithoutToken_DoesNotThrow()
        {
            // A launch-time token is optional in auth=required mode
            var dataArguments = new DataArguments(new string[0]);

            var act = () => _strategy.Validate(dataArguments);
            act.Should().NotThrow();
        }

        [Fact]
        public void Validate_WithToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });

            // Act & Assert
            var act = () => _strategy.Validate(dataArguments);
            act.Should().NotThrow();
        }

        [Fact]
        public void ConfigureAuthentication_WithToken_RequiresTokenAndSetsServerToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert
            options.RequireToken.Should().BeTrue();
            options.ServerToken.Should().Be("test-token");
        }

        [Fact]
        public void ConfigureAuthentication_WithoutToken_RequiresTokenButNoServerToken()
        {
            // Dynamic mode: RequireToken still true, but no static ServerToken
            var dataArguments = new DataArguments(new string[0]);
            var options = new TokenAuthenticationOptions();

            _strategy.ConfigureAuthentication(options, dataArguments);

            options.RequireToken.Should().BeTrue();
            options.ServerToken.Should().BeNull();
        }

        [Fact]
        public void OnPluginConnected_WithoutToken_DisconnectsImmediately()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-notoken-plugin";
            var disconnected = new List<string>();

            // Act
            _strategy.OnPluginConnected(typeof(McpServerHub), connectionId, null, logger,
                id => disconnected.Add(id));

            // Assert — tokenless plugin must be rejected, not registered
            disconnected.Should().Contain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().NotContain(connectionId);
        }

        [Fact]
        public void OnPluginConnected_WithWrongToken_WhenServerTokenConfigured_DisconnectsImmediately()
        {
            // Arrange — configure strategy with an explicit server token
            var strategy = new RequiredAuthMcpStrategy();
            strategy.ConfigureAuthentication(new TokenAuthenticationOptions(), new DataArguments(new[] { "token=server-secret" }));

            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-wrongtoken";
            var disconnected = new List<string>();

            // Act — plugin provides the wrong token
            strategy.OnPluginConnected(typeof(McpServerHub), connectionId, "wrong-token", logger,
                id => disconnected.Add(id));

            // Assert — must be rejected and not registered
            disconnected.Should().Contain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().NotContain(connectionId);
        }

        [Fact]
        public void OnPluginConnected_WithCorrectToken_WhenServerTokenConfigured_Registers()
        {
            // Arrange — configure strategy with an explicit server token
            var strategy = new RequiredAuthMcpStrategy();
            strategy.ConfigureAuthentication(new TokenAuthenticationOptions(), new DataArguments(new[] { "token=server-secret" }));

            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-correcttoken";
            var disconnected = new List<string>();

            // Act — plugin provides the matching token
            strategy.OnPluginConnected(typeof(McpServerHub), connectionId, "server-secret", logger,
                id => disconnected.Add(id));

            // Assert — must be accepted and registered
            disconnected.Should().BeEmpty();
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().Contain(connectionId);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void OnPluginConnected_DoesNotDisconnectOthers()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var existingId = "conn-remote-existing";
            var newId = "conn-remote-new";
            var disconnected = new List<string>();

            ClientUtils.AddClient(typeof(McpServerHub), existingId, logger, "token-1");

            // Act
            _strategy.OnPluginConnected(typeof(McpServerHub), newId, "token-2", logger,
                id => disconnected.Add(id));

            // Assert - auth-required mode should NOT disconnect existing clients
            disconnected.Should().BeEmpty();
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().Contain(existingId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().Contain(newId);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), existingId, logger);
            ClientUtils.RemoveClient(typeof(McpServerHub), newId, logger);
        }

        [Fact]
        public void ResolveConnectionId_WithToken_ReturnsTokenConnection()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-token";
            var token = "resolve-token-remote";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            // Act
            var result = _strategy.ResolveConnectionId(token, 0);

            // Assert
            result.Should().Be(connectionId);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ShouldNotifySession_MatchingToken_ReturnsTrue()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-notify";
            var token = "notify-token";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            // Act
            var result = _strategy.ShouldNotifySession(connectionId, token);

            // Assert
            result.Should().BeTrue();

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ShouldNotifySession_DifferentToken_ReturnsFalse()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-diff";
            var token = "token-A";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            // Act
            var result = _strategy.ShouldNotifySession(connectionId, "token-B");

            // Assert
            result.Should().BeFalse();

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ShouldNotifySession_NullPluginToken_ReturnsFalse()
        {
            // Arrange - connection without a token
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-notoken";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger);

            // Act
            var result = _strategy.ShouldNotifySession(connectionId, "some-session");

            // Assert
            result.Should().BeFalse();

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void GetClientData_UsesTokenScoping()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-client-data";
            var token = "client-data-token";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            var sessionTracker = new Mock<IMcpSessionTracker>();
            var expectedData = new McpClientData { IsConnected = true, ClientName = "remote-client" };
            sessionTracker.Setup(s => s.GetClientData(token)).Returns(expectedData);

            // Act
            var result = _strategy.GetClientData(connectionId, sessionTracker.Object);

            // Assert
            result.Should().BeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetClientData(token), Times.Once);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void GetServerData_UsesTokenScoping()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-remote-server-data";
            var token = "server-data-token";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            var sessionTracker = new Mock<IMcpSessionTracker>();
            var expectedData = new McpServerData { IsAiAgentConnected = true };
            sessionTracker.Setup(s => s.GetServerData(token)).Returns(expectedData);

            // Act
            var result = _strategy.GetServerData(connectionId, sessionTracker.Object);

            // Assert
            result.Should().BeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetServerData(token), Times.Once);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ResolveConnectionId_WithUnknownToken_ReturnsNull()
        {
            // auth-required mode must not fall back to any connection when the token is unknown
            var result = _strategy.ResolveConnectionId("unknown-token", 0);

            result.Should().BeNull();
        }

        [Fact]
        public void GetClientData_WithoutToken_ReturnsEmptyData()
        {
            // Arrange - connection registered without a token
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-notoken-client";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger);

            var sessionTracker = new Mock<IMcpSessionTracker>();

            // Act
            var result = _strategy.GetClientData(connectionId, sessionTracker.Object);

            // Assert — unscoped fallback must not be called; empty data returned instead
            result.IsConnected.Should().BeFalse();
            sessionTracker.Verify(s => s.GetClientData(), Times.Never);
            sessionTracker.Verify(s => s.GetClientData(It.IsAny<string>()), Times.Never);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void GetServerData_WithoutToken_ReturnsEmptyData()
        {
            // Arrange - connection registered without a token
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-notoken-server";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger);

            var sessionTracker = new Mock<IMcpSessionTracker>();

            // Act
            var result = _strategy.GetServerData(connectionId, sessionTracker.Object);

            // Assert — unscoped fallback must not be called; empty data returned instead
            result.IsAiAgentConnected.Should().BeFalse();
            sessionTracker.Verify(s => s.GetServerData(), Times.Never);
            sessionTracker.Verify(s => s.GetServerData(It.IsAny<string>()), Times.Never);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }
    }
}
