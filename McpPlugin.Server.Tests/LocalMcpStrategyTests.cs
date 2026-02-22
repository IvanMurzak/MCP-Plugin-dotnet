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
    public class LocalMcpStrategyTests
    {
        private readonly LocalMcpStrategy _strategy = new();

        [Fact]
        public void DeploymentMode_ReturnsLocal()
        {
            _strategy.DeploymentMode.Should().Be(Consts.MCP.Server.AuthOption.none);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsFalse()
        {
            _strategy.AllowMultipleConnections.Should().BeFalse();
        }

        [Fact]
        public void Validate_WithoutToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);

            // Act & Assert
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
        public void ConfigureAuthentication_WithoutToken_DoesNotRequireToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert
            options.RequireToken.Should().BeFalse();
            options.ServerToken.Should().BeNull();
        }

        [Fact]
        public void ConfigureAuthentication_WithToken_RequiresToken()
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
        public void OnPluginConnected_RegistersClient()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-local-1";
            var disconnected = new List<string>();

            // Act
            _strategy.OnPluginConnected(typeof(McpServerHub), connectionId, null, logger,
                id => disconnected.Add(id));

            // Assert - client should be registered
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().Contain(connectionId);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void OnPluginConnected_DisconnectsOtherClients()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var existingId = "conn-local-existing";
            var newId = "conn-local-new";
            var disconnected = new List<string>();

            ClientUtils.AddClient(typeof(McpServerHub), existingId, logger);

            // Act
            _strategy.OnPluginConnected(typeof(McpServerHub), newId, null, logger,
                id => disconnected.Add(id));

            // Assert
            disconnected.Should().Contain(existingId);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), existingId, logger);
            ClientUtils.RemoveClient(typeof(McpServerHub), newId, logger);
        }

        [Fact]
        public void OnPluginDisconnected_RemovesClient()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-local-remove";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger);

            // Act
            _strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, logger);

            // Assert
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).Should().NotContain(connectionId);
        }

        [Fact]
        public void ShouldNotifySession_AlwaysReturnsTrue()
        {
            // LOCAL mode broadcasts to all sessions
            _strategy.ShouldNotifySession("any-connection", "any-session").Should().BeTrue();
            _strategy.ShouldNotifySession("conn1", "session2").Should().BeTrue();
        }

        [Fact]
        public void GetClientData_ReturnsFirstAvailable()
        {
            // Arrange
            var sessionTracker = new Mock<IMcpSessionTracker>();
            var expectedData = new McpClientData { IsConnected = true, ClientName = "test" };
            sessionTracker.Setup(s => s.GetClientData()).Returns(expectedData);

            // Act
            var result = _strategy.GetClientData("some-connection", sessionTracker.Object);

            // Assert
            result.Should().BeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetClientData(), Times.Once);
        }

        [Fact]
        public void GetServerData_ReturnsFirstAvailable()
        {
            // Arrange
            var sessionTracker = new Mock<IMcpSessionTracker>();
            var expectedData = new McpServerData { IsAiAgentConnected = true };
            sessionTracker.Setup(s => s.GetServerData()).Returns(expectedData);

            // Act
            var result = _strategy.GetServerData("some-connection", sessionTracker.Object);

            // Assert
            result.Should().BeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetServerData(), Times.Once);
        }
    }
}
