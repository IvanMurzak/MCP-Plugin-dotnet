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
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class NoAuthMcpStrategyTests
    {
        private readonly NoAuthMcpStrategy _strategy = new();

        [Fact]
        public void AuthOption_ReturnsNone()
        {
            _strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.none);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsFalse()
        {
            _strategy.AllowMultipleConnections.ShouldBeFalse();
        }

        [Fact]
        public void Validate_WithoutToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);

            // Act & Assert
            Should.NotThrow(() => _strategy.Validate(dataArguments));
        }

        [Fact]
        public void Validate_WithToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });

            // Act & Assert
            Should.NotThrow(() => _strategy.Validate(dataArguments));
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
            options.RequireToken.ShouldBeFalse();
            options.ServerToken.ShouldBeNull();
        }

        [Fact]
        public void ConfigureAuthentication_WithToken_DoesNotRequireToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert — no-auth mode never gates the HTTP endpoint, even if a token is supplied
            options.RequireToken.ShouldBeFalse();
            options.ServerToken.ShouldBeNull();
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
                (id, _) => disconnected.Add(id));

            // Assert - client should be registered
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(connectionId);

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
                (id, _) => disconnected.Add(id));

            // Assert
            disconnected.ShouldContain(existingId);

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
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
        }

        [Fact]
        public void ShouldNotifySession_AlwaysReturnsTrue()
        {
            // no-auth mode broadcasts to all sessions
            _strategy.ShouldNotifySession("any-connection", "any-session").ShouldBeTrue();
            _strategy.ShouldNotifySession("conn1", "session2").ShouldBeTrue();
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
            result.ShouldBeSameAs(expectedData);
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
            result.ShouldBeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetServerData(), Times.Once);
        }
    }
}
