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
    public class RemoteMcpStrategyTests
    {
        private readonly RemoteMcpStrategy _strategy = new();

        [Fact]
        public void DeploymentMode_ReturnsRemote()
        {
            _strategy.AuthOption.Should().Be(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsTrue()
        {
            _strategy.AllowMultipleConnections.Should().BeTrue();
        }

        [Fact]
        public void Validate_WithoutToken_ThrowsInvalidOperationException()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);

            // Act & Assert
            var act = () => _strategy.Validate(dataArguments);
            act.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("REMOTE deployment mode requires a token");
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
        public void ConfigureAuthentication_AlwaysRequiresToken()
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

            // Assert - REMOTE mode should NOT disconnect existing clients
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
    }
}
