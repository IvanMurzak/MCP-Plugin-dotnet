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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class InitialClientDataTests
    {
        // ─── Helper ───────────────────────────────────────────────────────────────

        private static McpSessionTracker CreateTracker()
        {
            var logger = new Mock<ILogger<McpSessionTracker>>().Object;
            var dataArgs = new Mock<IDataArguments>();
            dataArgs.Setup(x => x.ClientTransport).Returns(Consts.MCP.Server.TransportMethod.stdio);
            var version = new Common.Version { Api = "1.0.0", Plugin = "1.0.0" };
            return new McpSessionTracker(logger, dataArgs.Object, version);
        }

        // ─── Interface contract ───────────────────────────────────────────────────

        [Fact]
        public void IClientMcpRpc_HasOnInitialClientData_Method()
        {
            // Act
            var method = typeof(IClientMcpRpc).GetMethod(nameof(IClientMcpRpc.OnInitialClientData));

            // Assert
            method.ShouldNotBeNull();
            method!.ReturnType.ShouldBe(typeof(Task));
            var parameters = method.GetParameters();
            parameters.Length.ShouldBe(1);
            parameters[0].ParameterType.ShouldBe(typeof(McpClientData[]));
        }

        // ─── NoAuthMcpStrategy ────────────────────────────────────────────────────

        [Fact]
        public void InitialClientData_NoAuthStrategy_NoSessions_ReturnsEmptyArray()
        {
            // Arrange
            var tracker = CreateTracker();
            var strategy = new NoAuthMcpStrategy();

            // Act
            var result = strategy.GetAllClientData("any-connection", tracker);

            // Assert
            result.ShouldBeEmpty();
        }

        [Fact]
        public void InitialClientData_NoAuthStrategy_ReturnsAllSessions()
        {
            // Arrange
            var tracker = CreateTracker();
            var strategy = new NoAuthMcpStrategy();

            tracker.Update("phys-1", null,
                new McpClientData { IsConnected = true, ClientName = "Claude" },
                new McpServerData());
            tracker.Update("phys-2", null,
                new McpClientData { IsConnected = true, ClientName = "GPT" },
                new McpServerData());

            // Act
            var result = strategy.GetAllClientData("any-connection", tracker);

            // Assert
            result.Count().ShouldBe(2);
            result.ShouldContain(c => c.ClientName == "Claude");
            result.ShouldContain(c => c.ClientName == "GPT");
        }

        [Fact]
        public void InitialClientData_NoAuthStrategy_IgnoresConnectionId()
        {
            // no-auth mode returns all sessions regardless of connection ID or token
            var tracker = CreateTracker();
            var strategy = new NoAuthMcpStrategy();

            tracker.Update("phys-1", "tokenA",
                new McpClientData { IsConnected = true, ClientName = "ClientA" },
                new McpServerData());
            tracker.Update("phys-2", "tokenB",
                new McpClientData { IsConnected = true, ClientName = "ClientB" },
                new McpServerData());

            var resultForConn1 = strategy.GetAllClientData("conn-1", tracker);
            var resultForConn2 = strategy.GetAllClientData("conn-999", tracker);

            resultForConn1.Count().ShouldBe(2);
            resultForConn2.Count().ShouldBe(2);
        }

        // ─── RequiredAuthMcpStrategy ─────────────────────────────────────────────

        [Fact]
        public void InitialClientData_RequiredAuthStrategy_ReturnsScopedSessions()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var tracker = CreateTracker();
            var strategy = new RequiredAuthMcpStrategy();

            var token = "test-token-scoped";
            var connId = "conn-initial-scoped-1";

            ClientUtils.AddClient<McpServerHub>(connId, logger, token);

            try
            {
                tracker.Update("phys-1", token,
                    new McpClientData { IsConnected = true, ClientName = "Claude" },
                    new McpServerData());
                tracker.Update("phys-2", "other-token",
                    new McpClientData { IsConnected = true, ClientName = "OtherClient" },
                    new McpServerData());

                // Act
                var result = strategy.GetAllClientData(connId, tracker);

                // Assert
                result.Count().ShouldBe(1);
                result[0].ClientName.ShouldBe("Claude");
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, logger);
            }
        }

        [Fact]
        public void InitialClientData_RequiredAuthStrategy_MultipleSessionsSameToken_ReturnsAll()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var tracker = CreateTracker();
            var strategy = new RequiredAuthMcpStrategy();

            var token = "shared-token-initial";
            var connId = "conn-initial-multi-1";

            ClientUtils.AddClient<McpServerHub>(connId, logger, token);

            try
            {
                tracker.Update("phys-A", token,
                    new McpClientData { IsConnected = true, ClientName = "ClientA" },
                    new McpServerData());
                tracker.Update("phys-B", token,
                    new McpClientData { IsConnected = true, ClientName = "ClientB" },
                    new McpServerData());
                tracker.Update("phys-C", "different-token",
                    new McpClientData { IsConnected = true, ClientName = "ClientC" },
                    new McpServerData());

                // Act
                var result = strategy.GetAllClientData(connId, tracker);

                // Assert — only the two sessions with matching token are returned
                result.Count().ShouldBe(2);
                result.ShouldContain(c => c.ClientName == "ClientA");
                result.ShouldContain(c => c.ClientName == "ClientB");
                result.ShouldNotContain(c => c.ClientName == "ClientC");
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, logger);
            }
        }

        [Fact]
        public void InitialClientData_RequiredAuthStrategy_NoToken_ReturnsEmptyArray()
        {
            // Arrange — connection registered without a token
            var logger = new Mock<ILogger>().Object;
            var tracker = CreateTracker();
            var strategy = new RequiredAuthMcpStrategy();

            var connId = "conn-initial-notoken-1";
            ClientUtils.AddClient<McpServerHub>(connId, logger);

            try
            {
                tracker.Update("phys-1", "some-token",
                    new McpClientData { IsConnected = true, ClientName = "Claude" },
                    new McpServerData());

                // Act
                var result = strategy.GetAllClientData(connId, tracker);

                // Assert — unscoped access denied; empty array returned
                result.ShouldBeEmpty();
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, logger);
            }
        }

        [Fact]
        public void InitialClientData_RequiredAuthStrategy_NoSessions_ReturnsEmptyArray()
        {
            // Arrange
            var logger = new Mock<ILogger>().Object;
            var tracker = CreateTracker();
            var strategy = new RequiredAuthMcpStrategy();

            var token = "token-empty-sessions";
            var connId = "conn-initial-empty-1";
            ClientUtils.AddClient<McpServerHub>(connId, logger, token);

            try
            {
                // Act — tracker is empty
                var result = strategy.GetAllClientData(connId, tracker);

                // Assert
                result.ShouldBeEmpty();
            }
            finally
            {
                ClientUtils.RemoveClient<McpServerHub>(connId, logger);
            }
        }
    }
}
