/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
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

        // ─── Parameterless queries (no sessions) ─────────────────────────────────

        [Fact]
        public void GetClientData_NoSessions_ReturnsDisconnected()
        {
            var result = _tracker.GetClientData();
            result.IsConnected.ShouldBeFalse();
        }

        [Fact]
        public void GetServerData_NoSessions_ReturnsFallbackData()
        {
            var result = _tracker.GetServerData();
            result.IsAiAgentConnected.ShouldBeFalse();
            result.ServerApiVersion.ShouldBe("1.0.0");
            result.ServerTransport.ShouldBe(Consts.MCP.Server.TransportMethod.stdio);
        }

        // ─── Update / GetClientData(physicalId) ──────────────────────────────────

        [Fact]
        public void Update_AddNewSession_CanBeRetrievedByPhysicalId()
        {
            // Arrange
            var clientData = new McpClientData { IsConnected = true, ClientName = "TestClient" };
            var serverData = new McpServerData { IsAiAgentConnected = true, ServerVersion = "2.0.0" };

            // Act
            _tracker.Update("phys-1", null, clientData, serverData);

            // Assert
            var retrievedClient = _tracker.GetClientData("phys-1");
            retrievedClient.IsConnected.ShouldBeTrue();
            retrievedClient.ClientName.ShouldBe("TestClient");

            var retrievedServer = _tracker.GetServerData("phys-1");
            retrievedServer.IsAiAgentConnected.ShouldBeTrue();
            retrievedServer.ServerVersion.ShouldBe("2.0.0");
        }

        [Fact]
        public void GetClientData_ByPhysicalId_UnknownSession_ReturnsDisconnected()
        {
            _tracker.GetClientData("nonexistent").IsConnected.ShouldBeFalse();
        }

        [Fact]
        public void GetServerData_ByPhysicalId_UnknownSession_ReturnsFallback()
        {
            _tracker.GetServerData("nonexistent").IsAiAgentConnected.ShouldBeFalse();
        }

        [Fact]
        public void Update_ExistingPhysicalId_OverwritesData()
        {
            // Arrange
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = true, ClientName = "First" }, new McpServerData { IsAiAgentConnected = true });
            // Act
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = true, ClientName = "Updated" }, new McpServerData { IsAiAgentConnected = false });
            // Assert
            _tracker.GetClientData("phys-1").ClientName.ShouldBe("Updated");
            _tracker.GetServerData("phys-1").IsAiAgentConnected.ShouldBeFalse();
        }

        // ─── Remove / ref-counting ────────────────────────────────────────────────

        [Fact]
        public void Remove_WithoutAddRef_ReturnsTrue_AndSessionNoLongerRetrievable()
        {
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = true, ClientName = "ToRemove" }, new McpServerData { IsAiAgentConnected = true });

            var result = _tracker.Remove("phys-1");

            result.ShouldBeTrue();
            _tracker.GetClientData("phys-1").IsConnected.ShouldBeFalse();
            _tracker.GetServerData("phys-1").IsAiAgentConnected.ShouldBeFalse();
        }

        [Fact]
        public void Remove_NonexistentSession_ReturnsTrueAndDoesNotThrow()
        {
            bool result = false;
            Should.NotThrow(() => result = _tracker.Remove("nonexistent"));
            result.ShouldBeTrue();
        }

        [Fact]
        public void AddRef_ThenRemove_SingleHolder_ReturnsTrue()
        {
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = true }, new McpServerData());
            _tracker.AddRef("phys-1");

            var result = _tracker.Remove("phys-1");

            result.ShouldBeTrue();
            _tracker.GetClientData("phys-1").IsConnected.ShouldBeFalse();
        }

        [Fact]
        public void AddRef_Twice_FirstRemove_ReturnsFalse_SessionStillRetrievable()
        {
            // Same physical ID, two concurrent connections (e.g. reconnect before timeout)
            _tracker.Update("phys-A", null, new McpClientData { IsConnected = true, ClientName = "Client" }, new McpServerData());
            _tracker.AddRef("phys-A"); // connection 1
            _tracker.AddRef("phys-A"); // connection 2 (same physical ID scenario)

            var firstResult = _tracker.Remove("phys-A"); // connection 1 disconnects

            firstResult.ShouldBeFalse();
            _tracker.GetClientData("phys-A").IsConnected.ShouldBeTrue();
        }

        [Fact]
        public void AddRef_Twice_SecondRemove_ReturnsTrue_SessionGone()
        {
            _tracker.Update("phys-A", null, new McpClientData { IsConnected = true, ClientName = "Client" }, new McpServerData());
            _tracker.AddRef("phys-A");
            _tracker.AddRef("phys-A");

            var firstResult  = _tracker.Remove("phys-A");
            var secondResult = _tracker.Remove("phys-A");

            firstResult.ShouldBeFalse();
            secondResult.ShouldBeTrue();
            _tracker.GetClientData("phys-A").IsConnected.ShouldBeFalse();
        }

        // ─── Parameterless queries (multiple sessions) ────────────────────────────

        [Fact]
        public void GetClientData_Parameterless_ReturnsFirstConnected()
        {
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = false, ClientName = "Disconnected" }, new McpServerData());
            _tracker.Update("phys-2", null, new McpClientData { IsConnected = true,  ClientName = "Connected"    }, new McpServerData());

            var result = _tracker.GetClientData();

            result.IsConnected.ShouldBeTrue();
            result.ClientName.ShouldBe("Connected");
        }

        [Fact]
        public void GetServerData_Parameterless_ReturnsStoredConnectedData()
        {
            var serverData = new McpServerData
            {
                IsAiAgentConnected = true,
                ServerVersion      = "3.0.0",
                ServerApiVersion   = "2.0.0",
                ServerTransport    = Consts.MCP.Server.TransportMethod.streamableHttp
            };
            _tracker.Update("phys-1", null, new McpClientData(), serverData);

            var result = _tracker.GetServerData();

            result.IsAiAgentConnected.ShouldBeTrue();
            result.ServerVersion.ShouldBe("3.0.0");
            result.ServerApiVersion.ShouldBe("2.0.0");
            result.ServerTransport.ShouldBe(Consts.MCP.Server.TransportMethod.streamableHttp);
        }

        // ─── GetAllClientData (no filter) ─────────────────────────────────────────

        [Fact]
        public void GetAllClientData_NoFilter_ReturnsAllSessions()
        {
            _tracker.Update("phys-1", "tokenA", new McpClientData { ClientName = "A" }, new McpServerData());
            _tracker.Update("phys-2", "tokenA", new McpClientData { ClientName = "B" }, new McpServerData());
            _tracker.Update("phys-3", "tokenB", new McpClientData { ClientName = "C" }, new McpServerData());

            _tracker.GetAllClientData().Count().ShouldBe(3);
        }

        // ─── GetAllClientData (routing-token filter) ──────────────────────────────

        [Fact]
        public void GetAllClientData_FilterByToken_ReturnsScopedSessions()
        {
            // Three MCP clients: two with tokenA (same shared server token) and one with tokenB
            _tracker.Update("phys-1", "tokenA", new McpClientData { IsConnected = true, ClientName = "A1" }, new McpServerData());
            _tracker.Update("phys-2", "tokenA", new McpClientData { IsConnected = true, ClientName = "A2" }, new McpServerData());
            _tracker.Update("phys-3", "tokenB", new McpClientData { IsConnected = true, ClientName = "B1" }, new McpServerData());

            var forA = _tracker.GetAllClientData("tokenA");
            var forB = _tracker.GetAllClientData("tokenB");

            forA.Count().ShouldBe(2);
            forA.ShouldContain(x => x.ClientName == "A1");
            forA.ShouldContain(x => x.ClientName == "A2");

            forB.Count().ShouldBe(1);
            forB.ShouldContain(x => x.ClientName == "B1");
        }

        [Fact]
        public void GetAllClientData_FilterByToken_AfterOneDisconnects_ShowsRemaining()
        {
            // Two clients sharing tokenA, one disconnects
            _tracker.Update("phys-1", "tokenA", new McpClientData { IsConnected = true, ClientName = "A1" }, new McpServerData());
            _tracker.Update("phys-2", "tokenA", new McpClientData { IsConnected = true, ClientName = "A2" }, new McpServerData());
            _tracker.AddRef("phys-1");
            _tracker.AddRef("phys-2");

            _tracker.Remove("phys-1"); // phys-1 disconnects

            var remaining = _tracker.GetAllClientData("tokenA");

            remaining.Count().ShouldBe(1);
            remaining.ShouldContain(x => x.ClientName == "A2");
        }

        // ─── GetClientDataByToken ─────────────────────────────────────────────────

        [Fact]
        public void GetClientDataByToken_ReturnsConnectedEntry()
        {
            _tracker.Update("phys-1", "tokenA", new McpClientData { IsConnected = false, ClientName = "Offline" }, new McpServerData());
            _tracker.Update("phys-2", "tokenA", new McpClientData { IsConnected = true,  ClientName = "Online"  }, new McpServerData());

            var result = _tracker.GetClientDataByToken("tokenA");

            result.ClientName.ShouldBe("Online");
        }

        [Fact]
        public void GetClientDataByToken_NullToken_FallsBackToParameterless()
        {
            _tracker.Update("phys-1", null, new McpClientData { IsConnected = true, ClientName = "NoToken" }, new McpServerData());

            var result = _tracker.GetClientDataByToken(null);

            result.ClientName.ShouldBe("NoToken");
        }

        // ─── Multi-physical-ID per routing token (core multi-client scenario) ─────

        [Fact]
        public void MultiplePhysicalIds_SameToken_EachGetOwnEntry()
        {
            // Two HTTP connections, same Bearer token (shared server token scenario)
            _tracker.Update("phys-1", "sharedToken", new McpClientData { IsConnected = true, ClientName = "ClientA" },
                new McpServerData { IsAiAgentConnected = true, ServerVersion = "1.0" });
            _tracker.Update("phys-2", "sharedToken", new McpClientData { IsConnected = true, ClientName = "ClientB" },
                new McpServerData { IsAiAgentConnected = false, ServerVersion = "2.0" });

            // Both entries exist independently
            _tracker.GetClientData("phys-1").ClientName.ShouldBe("ClientA");
            _tracker.GetClientData("phys-2").ClientName.ShouldBe("ClientB");

            // GetAllClientData scoped to the shared token returns both
            _tracker.GetAllClientData("sharedToken").Count().ShouldBe(2);
        }

        [Fact]
        public void MultipleSessions_IsolatedData()
        {
            _tracker.Update("phys-A", "tokenA", new McpClientData { IsConnected = true, ClientName = "ClientA" },
                new McpServerData { IsAiAgentConnected = true, ServerVersion = "1.0" });
            _tracker.Update("phys-B", "tokenB", new McpClientData { IsConnected = true, ClientName = "ClientB" },
                new McpServerData { IsAiAgentConnected = false, ServerVersion = "2.0" });

            _tracker.GetClientData("phys-A").ClientName.ShouldBe("ClientA");
            _tracker.GetClientData("phys-B").ClientName.ShouldBe("ClientB");
            _tracker.GetServerData("phys-A").ServerVersion.ShouldBe("1.0");
            _tracker.GetServerData("phys-B").ServerVersion.ShouldBe("2.0");
        }
    }
}
