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

        // Account-scoped (oauth) initial-client-data filtering is covered by AccountMcpStrategyTests;
        // the legacy RequiredAuthMcpStrategy token-scoped variant was deleted in mcp-authorize b5.
    }
}
