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
    /// <summary>
    /// The offline <c>token</c> auth strategy (mcp-authorize g6): single-connection, broadcast routing
    /// like <see cref="NoAuthMcpStrategy"/>, but gated at the hub on a single static secret with a
    /// constant-time compare.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class LocalTokenMcpStrategyTests
    {
        const string Secret = "s3cr3t-shared-token-value";

        static LocalTokenMcpStrategy NewConfiguredStrategy(string token = Secret)
        {
            var strategy = new LocalTokenMcpStrategy();
            strategy.Validate(new DataArguments(new[] { $"token={token}" }));
            return strategy;
        }

        [Fact]
        public void AuthOption_ReturnsToken()
        {
            new LocalTokenMcpStrategy().AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.token);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsFalse()
        {
            new LocalTokenMcpStrategy().AllowMultipleConnections.ShouldBeFalse();
        }

        [Fact]
        public void Validate_WithoutToken_Throws()
        {
            var strategy = new LocalTokenMcpStrategy();
            Should.Throw<ArgumentException>(() => strategy.Validate(new DataArguments(new string[0])));
        }

        [Fact]
        public void Validate_WithToken_DoesNotThrow()
        {
            var strategy = new LocalTokenMcpStrategy();
            Should.NotThrow(() => strategy.Validate(new DataArguments(new[] { $"token={Secret}" })));
        }

        [Fact]
        public void Validate_NonLoopbackBind_WarnsButDoesNotThrow()
        {
            // Owner ruling: a non-loopback bind is WARNED (LAN cleartext-token caveat) but ALLOWED.
            var strategy = new LocalTokenMcpStrategy();
            Should.NotThrow(() => strategy.Validate(new DataArguments(new[] { $"token={Secret}", "bind=any" })));
        }

        [Fact]
        public void ConfigureAuthentication_EnablesLocalTokenMode_WithSecret()
        {
            var strategy = new LocalTokenMcpStrategy();
            var options = new TokenAuthenticationOptions();

            strategy.ConfigureAuthentication(options, new DataArguments(new[] { $"token={Secret}" }));

            options.OAuthMode.ShouldBeFalse();
            options.LocalTokenMode.ShouldBeTrue();
            options.LocalToken.ShouldBe(Secret);
        }

        [Fact]
        public void OnPluginConnected_WithoutToken_DisconnectsCaller()
        {
            var strategy = NewConfiguredStrategy();
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-token-notoken";
            var disconnected = new List<string>();

            strategy.OnPluginConnected(typeof(McpServerHub), connectionId, null, logger,
                (id, _) => disconnected.Add(id));

            disconnected.ShouldContain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
        }

        [Fact]
        public void OnPluginConnected_WithWrongToken_DisconnectsCaller()
        {
            var strategy = NewConfiguredStrategy();
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-token-wrong";
            var disconnected = new List<string>();

            strategy.OnPluginConnected(typeof(McpServerHub), connectionId, "not-the-secret", logger,
                (id, _) => disconnected.Add(id));

            disconnected.ShouldContain(connectionId);
            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
        }

        [Fact]
        public void OnPluginConnected_WithMatchingToken_RegistersAndEnforcesSingleConnection()
        {
            var strategy = NewConfiguredStrategy();
            var logger = new Mock<ILogger>().Object;
            var existingId = "conn-token-existing";
            var newId = "conn-token-new";
            var disconnected = new List<string>();

            ClientUtils.AddClient(typeof(McpServerHub), existingId, logger, Secret);

            try
            {
                strategy.OnPluginConnected(typeof(McpServerHub), newId, Secret, logger,
                    (id, _) => disconnected.Add(id));

                ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(newId);
                // Single-connection invariant: the previous plugin is disconnected + removed.
                disconnected.ShouldContain(existingId);
                ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(existingId);
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), existingId, logger);
                ClientUtils.RemoveClient(typeof(McpServerHub), newId, logger);
            }
        }

        [Fact]
        public void OnPluginDisconnected_RemovesClient()
        {
            var strategy = NewConfiguredStrategy();
            var logger = new Mock<ILogger>().Object;
            var connectionId = "conn-token-remove";
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, logger, Secret);

            strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, logger);

            ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldNotContain(connectionId);
        }

        [Fact]
        public void ShouldNotifySession_AlwaysReturnsTrue()
        {
            var strategy = NewConfiguredStrategy();
            strategy.ShouldNotifySession("any-connection", "any-session").ShouldBeTrue();
        }

        [Fact]
        public void ResolveNotificationTarget_AlwaysBroadcasts()
        {
            var strategy = NewConfiguredStrategy();
            foreach (var probe in new[] { (string?)null, "", "any-token" })
                strategy.ResolveNotificationTarget(probe).Kind.ShouldBe(NotificationTarget.TargetKind.Broadcast);
        }

        [Fact]
        public void GetClientData_DelegatesToSessionTracker()
        {
            var strategy = NewConfiguredStrategy();
            var tracker = new Mock<IMcpSessionTracker>();
            var expected = new McpClientData { IsConnected = true, ClientName = "test" };
            tracker.Setup(t => t.GetClientData()).Returns(expected);

            strategy.GetClientData("any-connection", tracker.Object).ShouldBeSameAs(expected);
            tracker.Verify(t => t.GetClientData(), Times.Once);
        }

        [Fact]
        public void GetServerData_DelegatesToSessionTracker()
        {
            var strategy = NewConfiguredStrategy();
            var tracker = new Mock<IMcpSessionTracker>();
            var expected = new McpServerData { IsAiAgentConnected = true };
            tracker.Setup(t => t.GetServerData()).Returns(expected);

            strategy.GetServerData("any-connection", tracker.Object).ShouldBeSameAs(expected);
            tracker.Verify(t => t.GetServerData(), Times.Once);
        }
    }
}
