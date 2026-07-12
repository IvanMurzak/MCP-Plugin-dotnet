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
    ///   - auth=none: notifications broadcast to all (single-plugin invariant).
    /// The account-scoped (oauth) routing plane is covered by <see cref="AccountMcpStrategyTests"/>;
    /// the legacy auth=required token routing was deleted in mcp-authorize b5.
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
