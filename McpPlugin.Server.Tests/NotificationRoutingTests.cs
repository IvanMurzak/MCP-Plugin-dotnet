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
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Tests for the token-scoped notification routing pattern used in McpServerService.
    /// Verifies that when a token-mapped plugin exists, notifications target only that
    /// connection rather than broadcasting to all.
    /// </summary>
    public class NotificationRoutingTests
    {
        readonly ILogger _logger = new Mock<ILogger>().Object;

        static string UniqueId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Simulates the notification routing logic from McpServerService.NotifyClientConnectedAsync.
        /// </summary>
        async Task SimulateNotifyClientConnected(
            string sessionId,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext,
            McpClientData clientData)
        {
            var connectionId = ClientUtils.GetConnectionIdByToken(sessionId);
            if (connectionId != null)
                await hubContext.Clients.Client(connectionId).OnMcpClientConnected(clientData);
            else
                await hubContext.Clients.All.OnMcpClientConnected(clientData);
        }

        /// <summary>
        /// Simulates the notification routing logic from McpServerService.NotifyClientDisconnectedAsync.
        /// </summary>
        async Task SimulateNotifyClientDisconnected(
            string sessionId,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext)
        {
            var connectionId = ClientUtils.GetConnectionIdByToken(sessionId);
            if (connectionId != null)
                await hubContext.Clients.Client(connectionId).OnMcpClientDisconnected();
            else
                await hubContext.Clients.All.OnMcpClientDisconnected();
        }

        [Fact]
        public async Task NotifyConnected_WithToken_TargetsSpecificPlugin()
        {
            // Arrange
            var token = UniqueId();
            var connId = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connId, _logger, token);

            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };
            var targetClient = new Mock<IClientMcpRpc>();
            var allClients = new Mock<IClientMcpRpc>();

            var hubClients = new Mock<IHubClients<IClientMcpRpc>>();
            hubClients.Setup(c => c.Client(connId)).Returns(targetClient.Object);
            hubClients.Setup(c => c.All).Returns(allClients.Object);

            var hubContext = new Mock<IHubContext<McpServerHub, IClientMcpRpc>>();
            hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

            try
            {
                // Act
                await SimulateNotifyClientConnected(token, hubContext.Object, clientData);

                // Assert — targeted client was notified, not all
                targetClient.Verify(c => c.OnMcpClientConnected(clientData), Times.Once);
                allClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, _logger);
            }
        }

        [Fact]
        public async Task NotifyConnected_WithoutToken_BroadcastsToAll()
        {
            // Arrange
            var sessionId = "stdio"; // no token mapping exists for this
            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };
            var allClients = new Mock<IClientMcpRpc>();

            var hubClients = new Mock<IHubClients<IClientMcpRpc>>();
            hubClients.Setup(c => c.All).Returns(allClients.Object);

            var hubContext = new Mock<IHubContext<McpServerHub, IClientMcpRpc>>();
            hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

            // Act
            await SimulateNotifyClientConnected(sessionId, hubContext.Object, clientData);

            // Assert — all clients were notified
            allClients.Verify(c => c.OnMcpClientConnected(clientData), Times.Once);
        }

        [Fact]
        public async Task NotifyDisconnected_WithToken_TargetsSpecificPlugin()
        {
            // Arrange
            var token = UniqueId();
            var connId = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connId, _logger, token);

            var targetClient = new Mock<IClientMcpRpc>();
            var allClients = new Mock<IClientMcpRpc>();

            var hubClients = new Mock<IHubClients<IClientMcpRpc>>();
            hubClients.Setup(c => c.Client(connId)).Returns(targetClient.Object);
            hubClients.Setup(c => c.All).Returns(allClients.Object);

            var hubContext = new Mock<IHubContext<McpServerHub, IClientMcpRpc>>();
            hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

            try
            {
                // Act
                await SimulateNotifyClientDisconnected(token, hubContext.Object);

                // Assert
                targetClient.Verify(c => c.OnMcpClientDisconnected(), Times.Once);
                allClients.Verify(c => c.OnMcpClientDisconnected(), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, _logger);
            }
        }

        [Fact]
        public async Task NotifyDisconnected_WithoutToken_BroadcastsToAll()
        {
            // Arrange
            var sessionId = "stdio";
            var allClients = new Mock<IClientMcpRpc>();

            var hubClients = new Mock<IHubClients<IClientMcpRpc>>();
            hubClients.Setup(c => c.All).Returns(allClients.Object);

            var hubContext = new Mock<IHubContext<McpServerHub, IClientMcpRpc>>();
            hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

            // Act
            await SimulateNotifyClientDisconnected(sessionId, hubContext.Object);

            // Assert
            allClients.Verify(c => c.OnMcpClientDisconnected(), Times.Once);
        }

        [Fact]
        public async Task TwoPlugins_OnlyMatchingTokenReceivesNotification()
        {
            // Arrange
            var tokenA = UniqueId();
            var tokenB = UniqueId();
            var connA = UniqueId();
            var connB = UniqueId();
            ClientUtils.AddClient<McpServerHub>(connA, _logger, tokenA);
            ClientUtils.AddClient<McpServerHub>(connB, _logger, tokenB);

            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };
            var clientA = new Mock<IClientMcpRpc>();
            var clientB = new Mock<IClientMcpRpc>();
            var allClients = new Mock<IClientMcpRpc>();

            var hubClients = new Mock<IHubClients<IClientMcpRpc>>();
            hubClients.Setup(c => c.Client(connA)).Returns(clientA.Object);
            hubClients.Setup(c => c.Client(connB)).Returns(clientB.Object);
            hubClients.Setup(c => c.All).Returns(allClients.Object);

            var hubContext = new Mock<IHubContext<McpServerHub, IClientMcpRpc>>();
            hubContext.Setup(c => c.Clients).Returns(hubClients.Object);

            try
            {
                // Act — notify for token A's session
                await SimulateNotifyClientConnected(tokenA, hubContext.Object, clientData);

                // Assert — only plugin A receives the notification
                clientA.Verify(c => c.OnMcpClientConnected(clientData), Times.Once);
                clientB.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>()), Times.Never);
                allClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>()), Times.Never);
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connA, _logger);
                ClientUtils.RemoveClient<McpServerHub>(connB, _logger);
            }
        }
    }
}
