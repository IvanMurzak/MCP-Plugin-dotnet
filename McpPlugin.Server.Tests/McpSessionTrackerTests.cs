/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class McpSessionTrackerTests
    {
        readonly McpSessionTracker _tracker;

        public McpSessionTrackerTests()
        {
            var logger = new Mock<ILogger<McpSessionTracker>>();
            var dataArgs = new Mock<IDataArguments>();
            dataArgs.Setup(x => x.ClientTransport).Returns(Consts.MCP.Server.TransportMethod.stdio);
            var version = new Common.Version { Api = "1.0.0", Plugin = "1.0.0" };
            _tracker = new McpSessionTracker(logger.Object, dataArgs.Object, version);
        }

        [Fact]
        public void GetClientData_NoSessions_ReturnsDisconnected()
        {
            // Act
            var result = _tracker.GetClientData();

            // Assert
            result.IsConnected.Should().BeFalse();
        }

        [Fact]
        public void GetServerData_NoSessions_ReturnsFallbackData()
        {
            // Act
            var result = _tracker.GetServerData();

            // Assert
            result.IsAiAgentConnected.Should().BeFalse();
            result.ServerApiVersion.Should().Be("1.0.0");
            result.ServerTransport.Should().Be(Consts.MCP.Server.TransportMethod.stdio);
        }

        [Fact]
        public void Update_AddNewSession_CanBeRetrievedById()
        {
            // Arrange
            var clientData = new McpClientData { IsConnected = true, ClientName = "TestClient" };
            var serverData = new McpServerData { IsAiAgentConnected = true, ServerVersion = "2.0.0" };

            // Act
            _tracker.Update("session-1", clientData, serverData);

            // Assert
            var retrievedClient = _tracker.GetClientData("session-1");
            retrievedClient.IsConnected.Should().BeTrue();
            retrievedClient.ClientName.Should().Be("TestClient");

            var retrievedServer = _tracker.GetServerData("session-1");
            retrievedServer.IsAiAgentConnected.Should().BeTrue();
            retrievedServer.ServerVersion.Should().Be("2.0.0");
        }

        [Fact]
        public void GetClientData_BySessionId_UnknownSession_ReturnsDisconnected()
        {
            // Act
            var result = _tracker.GetClientData("nonexistent");

            // Assert
            result.IsConnected.Should().BeFalse();
        }

        [Fact]
        public void GetServerData_BySessionId_UnknownSession_ReturnsFallback()
        {
            // Act
            var result = _tracker.GetServerData("nonexistent");

            // Assert
            result.IsAiAgentConnected.Should().BeFalse();
        }

        [Fact]
        public void Update_ExistingSession_OverwritesData()
        {
            // Arrange
            var clientData1 = new McpClientData { IsConnected = true, ClientName = "First" };
            var serverData1 = new McpServerData { IsAiAgentConnected = true };
            _tracker.Update("session-1", clientData1, serverData1);

            var clientData2 = new McpClientData { IsConnected = true, ClientName = "Updated" };
            var serverData2 = new McpServerData { IsAiAgentConnected = false };

            // Act
            _tracker.Update("session-1", clientData2, serverData2);

            // Assert
            var retrieved = _tracker.GetClientData("session-1");
            retrieved.ClientName.Should().Be("Updated");

            var retrievedServer = _tracker.GetServerData("session-1");
            retrievedServer.IsAiAgentConnected.Should().BeFalse();
        }

        [Fact]
        public void Remove_WithoutAddRef_ReturnsTrue_AndSessionNoLongerRetrievable()
        {
            // Arrange
            var clientData = new McpClientData { IsConnected = true, ClientName = "ToRemove" };
            var serverData = new McpServerData { IsAiAgentConnected = true };
            _tracker.Update("session-1", clientData, serverData);

            // Act — Remove without prior AddRef: treated as single-holder, returns true
            var result = _tracker.Remove("session-1");

            // Assert
            result.Should().BeTrue();
            _tracker.GetClientData("session-1").IsConnected.Should().BeFalse();
            _tracker.GetServerData("session-1").IsAiAgentConnected.Should().BeFalse();
        }

        [Fact]
        public void Remove_NonexistentSession_ReturnsTrueAndDoesNotThrow()
        {
            // Act & Assert
            bool result = false;
            var act = () => result = _tracker.Remove("nonexistent");
            act.Should().NotThrow();
            result.Should().BeTrue();
        }

        [Fact]
        public void AddRef_ThenRemove_SingleHolder_ReturnsTrue()
        {
            // Arrange
            _tracker.Update("session-1", new McpClientData { IsConnected = true }, new McpServerData());
            _tracker.AddRef("session-1");

            // Act
            var result = _tracker.Remove("session-1");

            // Assert
            result.Should().BeTrue();
            _tracker.GetClientData("session-1").IsConnected.Should().BeFalse();
        }

        [Fact]
        public void AddRef_Twice_FirstRemove_ReturnsFalse_SessionStillRetrievable()
        {
            // Arrange — two connections share the same sessionId
            _tracker.Update("token-A", new McpClientData { IsConnected = true, ClientName = "Client" }, new McpServerData());
            _tracker.AddRef("token-A"); // connection 1
            _tracker.AddRef("token-A"); // connection 2

            // Act — first connection disconnects
            var firstResult = _tracker.Remove("token-A");

            // Assert — second connection still active; no disconnect yet
            firstResult.Should().BeFalse();
            _tracker.GetClientData("token-A").IsConnected.Should().BeTrue();
        }

        [Fact]
        public void AddRef_Twice_SecondRemove_ReturnsTrue_SessionGone()
        {
            // Arrange — two connections share the same sessionId
            _tracker.Update("token-A", new McpClientData { IsConnected = true, ClientName = "Client" }, new McpServerData());
            _tracker.AddRef("token-A"); // connection 1
            _tracker.AddRef("token-A"); // connection 2

            // Act — both connections disconnect in sequence
            var firstResult  = _tracker.Remove("token-A"); // connection 1 ends
            var secondResult = _tracker.Remove("token-A"); // connection 2 ends

            // Assert — only the second Remove signals "last"
            firstResult.Should().BeFalse();
            secondResult.Should().BeTrue();
            _tracker.GetClientData("token-A").IsConnected.Should().BeFalse();
        }

        [Fact]
        public void GetClientData_Parameterless_ReturnsFirstConnected()
        {
            // Arrange
            _tracker.Update("session-1", new McpClientData { IsConnected = false, ClientName = "Disconnected" }, new McpServerData());
            _tracker.Update("session-2", new McpClientData { IsConnected = true, ClientName = "Connected" }, new McpServerData());

            // Act
            var result = _tracker.GetClientData();

            // Assert
            result.IsConnected.Should().BeTrue();
            result.ClientName.Should().Be("Connected");
        }

        [Fact]
        public void GetServerData_Parameterless_ReturnsStoredConnectedData()
        {
            // Arrange
            var serverData = new McpServerData
            {
                IsAiAgentConnected = true,
                ServerVersion = "3.0.0",
                ServerApiVersion = "2.0.0",
                ServerTransport = Consts.MCP.Server.TransportMethod.streamableHttp
            };
            _tracker.Update("session-1", new McpClientData(), serverData);

            // Act
            var result = _tracker.GetServerData();

            // Assert
            result.IsAiAgentConnected.Should().BeTrue();
            result.ServerVersion.Should().Be("3.0.0");
            result.ServerApiVersion.Should().Be("2.0.0");
            result.ServerTransport.Should().Be(Consts.MCP.Server.TransportMethod.streamableHttp);
        }

        [Fact]
        public void GetAllClientData_ReturnsAllSessions()
        {
            // Arrange
            _tracker.Update("session-1", new McpClientData { ClientName = "A" }, new McpServerData());
            _tracker.Update("session-2", new McpClientData { ClientName = "B" }, new McpServerData());
            _tracker.Update("session-3", new McpClientData { ClientName = "C" }, new McpServerData());

            // Act
            var result = _tracker.GetAllClientData();

            // Assert
            result.Should().HaveCount(3);
        }

        [Fact]
        public void MultipleSessions_IsolatedData()
        {
            // Arrange
            _tracker.Update("token-A", new McpClientData { IsConnected = true, ClientName = "ClientA" },
                new McpServerData { IsAiAgentConnected = true, ServerVersion = "1.0" });
            _tracker.Update("token-B", new McpClientData { IsConnected = true, ClientName = "ClientB" },
                new McpServerData { IsAiAgentConnected = false, ServerVersion = "2.0" });

            // Act & Assert
            _tracker.GetClientData("token-A").ClientName.Should().Be("ClientA");
            _tracker.GetClientData("token-B").ClientName.Should().Be("ClientB");
            _tracker.GetServerData("token-A").ServerVersion.Should().Be("1.0");
            _tracker.GetServerData("token-B").ServerVersion.Should().Be("2.0");
        }
    }
}
