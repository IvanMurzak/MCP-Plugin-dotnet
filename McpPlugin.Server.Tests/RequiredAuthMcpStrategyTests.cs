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
    public class RequiredAuthMcpStrategyTests
    {
        private readonly RequiredAuthMcpStrategy _strategy = new();

        [Fact]
        public void AuthOption_ReturnsRequired()
        {
            _strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsTrue()
        {
            _strategy.AllowMultipleConnections.ShouldBeTrue();
        }

        [Fact]
        public void Validate_WithoutToken_DoesNotThrow()
        {
            // A launch-time token is optional in auth=required mode
            var dataArguments = new DataArguments(new string[0]);

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
        public void ConfigureAuthentication_WithToken_RequiresTokenAndSetsServerToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert
            options.RequireToken.ShouldBeTrue();
            options.ServerToken.ShouldBe("test-token");
        }

        [Fact]
        public void ConfigureAuthentication_WithoutToken_RequiresTokenButNoServerToken()
        {
            // Dynamic mode: RequireToken still true, but no static ServerToken
            var dataArguments = new DataArguments(new string[0]);
            var options = new TokenAuthenticationOptions();

            _strategy.ConfigureAuthentication(options, dataArguments);

            options.RequireToken.ShouldBeTrue();
            options.ServerToken.ShouldBeNull();
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
                (id, _) => disconnected.Add(id));

            // Assert — tokenless plugin must be rejected, not registered
            disconnected.ShouldContain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
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
                (id, _) => disconnected.Add(id));

            // Assert — must be rejected and not registered
            disconnected.ShouldContain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
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
                (id, _) => disconnected.Add(id));

            // Assert — must be accepted and registered
            disconnected.ShouldBeEmpty();
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(connectionId);

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
                (id, _) => disconnected.Add(id));

            // Assert - auth-required mode should NOT disconnect existing clients
            disconnected.ShouldBeEmpty();
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(existingId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(newId);

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
            result.ShouldBe(connectionId);

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
            result.ShouldBeTrue();

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
            result.ShouldBeFalse();

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
            result.ShouldBeFalse();

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
            sessionTracker.Setup(s => s.GetClientDataByToken(token)).Returns(expectedData);

            // Act
            var result = _strategy.GetClientData(connectionId, sessionTracker.Object);

            // Assert
            result.ShouldBeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetClientDataByToken(token), Times.Once);

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
            sessionTracker.Setup(s => s.GetServerDataByToken(token)).Returns(expectedData);

            // Act
            var result = _strategy.GetServerData(connectionId, sessionTracker.Object);

            // Assert
            result.ShouldBeSameAs(expectedData);
            sessionTracker.Verify(s => s.GetServerDataByToken(token), Times.Once);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ResolveConnectionId_WithUnknownToken_ReturnsNull()
        {
            // auth-required mode must not fall back to any connection when the token is unknown
            var result = _strategy.ResolveConnectionId("unknown-token", 0);

            result.ShouldBeNull();
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
            result.IsConnected.ShouldBeFalse();
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
            result.IsAiAgentConnected.ShouldBeFalse();
            sessionTracker.Verify(s => s.GetServerData(), Times.Never);
            sessionTracker.Verify(s => s.GetServerData(It.IsAny<string>()), Times.Never);

            // Cleanup
            ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
        }

        [Fact]
        public void ResolveNotificationTarget_WithMatchingPluginToken_TargetsThatPlugin()
        {
            // Arrange — register a plugin with token T; the routing token of an MCP session is T.
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-notify-target";
            var token = "notify-target-token";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, token);

            try
            {
                // Act
                var target = _strategy.ResolveNotificationTarget(token);

                // Assert
                target.Kind.ShouldBe(NotificationTarget.TargetKind.Specific);
                target.ConnectionId.ShouldBe(connectionId);
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
            }
        }

        [Fact]
        public void ResolveNotificationTarget_WithUnmappedToken_DropsNotification()
        {
            // Regression for issue #102: Tier 2 (DCR) and Tier 3 (ServerToken) sessions
            // present a valid bearer token that is NOT in TokenToConnectionId. The notification
            // must be dropped — broadcasting would leak foreign clients into every plugin.
            var strategy = new RequiredAuthMcpStrategy();
            // No ConfigureAuthentication call → _serverToken is null (dynamic-pairing mode).

            var target = strategy.ResolveNotificationTarget("bearer-from-tier2-or-tier3-session");

            target.Kind.ShouldBe(NotificationTarget.TargetKind.Drop);
            target.ConnectionId.ShouldBeNull();
        }

        [Fact]
        public void ResolveNotificationTarget_WithNullTokenAndNoServerToken_Drops()
        {
            // Dynamic mode (no _serverToken configured) and no per-session token: there is
            // no addressable plugin. Broadcasting in this case is exactly the bug from #102.
            var strategy = new RequiredAuthMcpStrategy();

            var target = strategy.ResolveNotificationTarget(null);

            target.Kind.ShouldBe(NotificationTarget.TargetKind.Drop);
        }

        [Fact]
        public void ResolveNotificationTarget_WithNullTokenAndServerTokenConfigured_FallsBackToServerToken()
        {
            // Stdio transport never sets McpSessionTokenContext.CurrentToken, so the routing
            // token reaching ResolveNotificationTarget can be null. When the server has a token
            // configured (and a plugin has registered with it), notifications must still reach
            // that plugin. Mirrors ResolveConnectionId's stdio fallback.
            var strategy = new RequiredAuthMcpStrategy();
            strategy.ConfigureAuthentication(new TokenAuthenticationOptions(), new DataArguments(new[] { "token=stdio-server-token" }));

            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-required-stdio-fallback";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, "stdio-server-token");

            try
            {
                // Act
                var target = strategy.ResolveNotificationTarget(null);

                // Assert
                target.Kind.ShouldBe(NotificationTarget.TargetKind.Specific);
                target.ConnectionId.ShouldBe(connectionId);
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, logger);
            }
        }

        [Fact]
        public void ResolveNotificationTarget_NeverBroadcasts()
        {
            // Invariant: auth-required must never resolve to a broadcast — that is the bug.
            var strategy = new RequiredAuthMcpStrategy();
            strategy.ConfigureAuthentication(new TokenAuthenticationOptions(), new DataArguments(new[] { "token=ignored-for-this-check" }));

            foreach (var probe in new[] { (string?)null, "", "unknown", "another-unknown" })
            {
                var target = strategy.ResolveNotificationTarget(probe);
                target.Kind.ShouldNotBe(NotificationTarget.TargetKind.Broadcast,
                    customMessage: $"auth-required must never broadcast (probe='{probe ?? "<null>"}').");
            }
        }
    }
}
