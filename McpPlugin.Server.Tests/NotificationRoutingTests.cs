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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Tests for the strategy-driven notification routing pattern used in McpServerService.
    /// Verifies that the routing decision honors the active connection strategy:
    ///   - auth=required: notifications target the per-token plugin OR are dropped (no broadcast).
    ///   - auth=none: notifications broadcast to all (single-plugin invariant).
    /// Regression coverage for issue #102: foreign Tier 2 (DCR) and Tier 3 (ServerToken) sessions
    /// must not bleed into unrelated tenants' active-client lists via the broadcast fallback.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class NotificationRoutingTests
    {
        readonly ILogger _logger = new Mock<ILogger>().Object;

        static string UniqueId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Mirrors the dispatch shape of <see cref="McpServerService.NotifyClientConnectedAsync"/>:
        /// resolve the notification target via the strategy, then dispatch to the hub or drop.
        /// </summary>
        async Task SimulateNotifyClientConnected(
            IMcpConnectionStrategy strategy,
            string? routingToken,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext,
            McpClientData connectedClient,
            McpClientData[] allActiveClients)
        {
            var target = strategy.ResolveNotificationTarget(routingToken);
            switch (target.Kind)
            {
                case NotificationTarget.TargetKind.Specific:
                    await hubContext.Clients.Client(target.ConnectionId!).OnMcpClientConnected(connectedClient, allActiveClients);
                    break;
                case NotificationTarget.TargetKind.Broadcast:
                    await hubContext.Clients.All.OnMcpClientConnected(connectedClient, allActiveClients);
                    break;
                case NotificationTarget.TargetKind.Drop:
                    // intentional: no addressable recipient
                    break;
            }
        }

        async Task SimulateNotifyClientDisconnected(
            IMcpConnectionStrategy strategy,
            string? routingToken,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext,
            McpClientData disconnectedClient,
            McpClientData[] remainingClients)
        {
            var target = strategy.ResolveNotificationTarget(routingToken);
            switch (target.Kind)
            {
                case NotificationTarget.TargetKind.Specific:
                    await hubContext.Clients.Client(target.ConnectionId!).OnMcpClientDisconnected(disconnectedClient, remainingClients);
                    break;
                case NotificationTarget.TargetKind.Broadcast:
                    await hubContext.Clients.All.OnMcpClientDisconnected(disconnectedClient, remainingClients);
                    break;
                case NotificationTarget.TargetKind.Drop:
                    break;
            }
        }

        sealed class HubMocks
        {
            public Mock<IHubContext<McpServerHub, IClientMcpRpc>> HubContext { get; } = new();
            public Mock<IHubClients<IClientMcpRpc>> HubClients { get; } = new();
            public Mock<IClientMcpRpc> AllClients { get; } = new();

            public HubMocks()
            {
                HubClients.Setup(c => c.All).Returns(AllClients.Object);
                HubContext.Setup(c => c.Clients).Returns(HubClients.Object);
            }

            public Mock<IClientMcpRpc> RegisterClient(string connectionId)
            {
                var mock = new Mock<IClientMcpRpc>();
                HubClients.Setup(c => c.Client(connectionId)).Returns(mock.Object);
                return mock;
            }
        }

        // ----- auth=required -----------------------------------------------------------------

        [Fact]
        public async Task RequiredAuth_WithMatchingPluginToken_TargetsThatPluginOnly()
        {
            // Arrange
            var strategy = new RequiredAuthMcpStrategy();
            var token = UniqueId();
            var connId = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connId, _logger, token);

            var hub = new HubMocks();
            var targetClient = hub.RegisterClient(connId);
            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };
            try
            {
                // Act
                await SimulateNotifyClientConnected(strategy, token, hub.HubContext.Object, clientData, new[] { clientData });

                // Assert
                targetClient.Verify(c => c.OnMcpClientConnected(clientData, It.IsAny<McpClientData[]>()), Times.Once);
                hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, _logger);
            }
        }

        [Fact]
        public async Task RequiredAuth_WithUnmappedToken_DropsNotification_DoesNotBroadcast()
        {
            // Issue #102 reproduction: a Tier 2 (DCR) or Tier 3 (ServerToken) session's bearer
            // token is valid for HTTP auth but is NOT mapped to a plugin in TokenToConnectionId.
            // Pre-fix behavior: Clients.All received the notification, polluting every plugin's
            // active-client list. Post-fix behavior: notification is dropped.
            var strategy = new RequiredAuthMcpStrategy();
            // No ConfigureAuthentication → dynamic-pairing mode (no _serverToken fallback).

            // A plugin from another tenant is connected (would be the wrong recipient pre-fix).
            var unrelatedPluginConn = UniqueId();
            var unrelatedPluginToken = UniqueId();
            ClientUtils.AddClient<McpServerHub>(unrelatedPluginConn, _logger, unrelatedPluginToken);

            var hub = new HubMocks();
            var unrelatedClient = hub.RegisterClient(unrelatedPluginConn);
            var foreignClientData = new McpClientData { IsConnected = true, ClientName = "Claude (Tier 3 ServerToken)" };
            try
            {
                // Act — the foreign session's routing token does NOT match any plugin.
                var foreignSessionToken = UniqueId();
                await SimulateNotifyClientConnected(strategy, foreignSessionToken, hub.HubContext.Object, foreignClientData, new[] { foreignClientData });

                // Assert — dropped: no plugin was notified, including the unrelated tenant's plugin.
                hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
                unrelatedClient.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(unrelatedPluginConn, _logger);
            }
        }

        [Fact]
        public async Task RequiredAuth_DisconnectWithUnmappedToken_DropsNotification_DoesNotBroadcast()
        {
            // Disconnect-side mirror of the connect-side regression: a foreign Tier 2/Tier 3
            // session ending must not broadcast OnMcpClientDisconnected to unrelated plugins.
            var strategy = new RequiredAuthMcpStrategy();

            var unrelatedPluginConn = UniqueId();
            var unrelatedPluginToken = UniqueId();
            ClientUtils.AddClient<McpServerHub>(unrelatedPluginConn, _logger, unrelatedPluginToken);

            var hub = new HubMocks();
            var unrelatedClient = hub.RegisterClient(unrelatedPluginConn);
            var foreignClientData = new McpClientData { IsConnected = false };
            try
            {
                // Act
                var foreignSessionToken = UniqueId();
                await SimulateNotifyClientDisconnected(strategy, foreignSessionToken, hub.HubContext.Object, foreignClientData, Array.Empty<McpClientData>());

                // Assert — dropped: no broadcast, no targeted send to unrelated plugin.
                hub.AllClients.Verify(c => c.OnMcpClientDisconnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
                unrelatedClient.Verify(c => c.OnMcpClientDisconnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(unrelatedPluginConn, _logger);
            }
        }

        [Fact]
        public async Task RequiredAuth_TwoPlugins_OnlyMatchingTokenReceivesNotification()
        {
            // Multi-tenant isolation check: two plugins with distinct tokens; only the one whose
            // token matches the routing token of the notifying session receives the event.
            var strategy = new RequiredAuthMcpStrategy();
            var tokenA = UniqueId();
            var tokenB = UniqueId();
            var connA = UniqueId();
            var connB = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connA, _logger, tokenA);
            ClientUtils.AddClient<McpServerHub>(connB, _logger, tokenB);

            var hub = new HubMocks();
            var clientA = hub.RegisterClient(connA);
            var clientB = hub.RegisterClient(connB);
            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };
            try
            {
                // Act
                await SimulateNotifyClientConnected(strategy, tokenA, hub.HubContext.Object, clientData, new[] { clientData });

                // Assert
                clientA.Verify(c => c.OnMcpClientConnected(clientData, It.IsAny<McpClientData[]>()), Times.Once);
                clientB.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
                hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connA, _logger);
                ClientUtils.RemoveClient<McpServerHub>(connB, _logger);
            }
        }

        [Fact]
        public async Task RequiredAuth_StdioWithServerToken_FallsBackToServerTokenPlugin()
        {
            // Stdio transport never sets McpSessionTokenContext.CurrentToken, so the routing
            // token reaching the notification path is null. When a server token is configured
            // and a plugin registered with it, the notification must reach that plugin.
            var strategy = new RequiredAuthMcpStrategy();
            strategy.ConfigureAuthentication(new TokenAuthenticationOptions(), new DataArguments(new[] { "token=stdio-route-token" }));

            var connId = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connId, _logger, "stdio-route-token");

            var hub = new HubMocks();
            var stdioPlugin = hub.RegisterClient(connId);
            var clientData = new McpClientData { IsConnected = true, ClientName = "stdio" };
            try
            {
                // Act — null routing token simulates stdio.
                await SimulateNotifyClientConnected(strategy, null, hub.HubContext.Object, clientData, new[] { clientData });

                // Assert
                stdioPlugin.Verify(c => c.OnMcpClientConnected(clientData, It.IsAny<McpClientData[]>()), Times.Once);
                hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, _logger);
            }
        }

        // ----- auth=none ---------------------------------------------------------------------

        [Fact]
        public async Task NoAuth_BroadcastsToAll()
        {
            // no-auth mode enforces a single plugin connection, so Clients.All targets exactly
            // one recipient. Routing token is irrelevant in this mode.
            var strategy = new NoAuthMcpStrategy();
            var hub = new HubMocks();
            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };

            // Act
            await SimulateNotifyClientConnected(strategy, null, hub.HubContext.Object, clientData, new[] { clientData });

            // Assert
            hub.AllClients.Verify(c => c.OnMcpClientConnected(clientData, It.IsAny<McpClientData[]>()), Times.Once);
        }

        [Fact]
        public async Task NoAuth_DisconnectBroadcastsToAll()
        {
            var strategy = new NoAuthMcpStrategy();
            var hub = new HubMocks();
            var disconnectedData = new McpClientData { IsConnected = false };

            // Act
            await SimulateNotifyClientDisconnected(strategy, null, hub.HubContext.Object, disconnectedData, Array.Empty<McpClientData>());

            // Assert
            hub.AllClients.Verify(c => c.OnMcpClientDisconnected(disconnectedData, It.IsAny<McpClientData[]>()), Times.Once);
        }
    }
}
