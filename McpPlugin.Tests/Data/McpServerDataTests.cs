/*
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚  Author: Ivan Murzak (https://github.com/IvanMurzak)                   â”‚
â”‚  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  â”‚
â”‚  Copyright (c) 2025 Ivan Murzak                                        â”‚
â”‚  Licensed under the Apache License, Version 2.0.                       â”‚
â”‚  See the LICENSE file in the project root for more information.        â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
*/
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Data
{
    public class McpServerDataTests
    {
        [Fact]
        public void McpServerData_DefaultConstructor_InitializesWithDefaultValues()
        {
            // Act
            var serverData = new McpServerData();

            // Assert
            serverData.ServerVersion.ShouldBeNull();
            serverData.ServerApiVersion.ShouldBeNull();
            serverData.ServerTransport.ShouldBe(Consts.MCP.Server.TransportMethod.unknown);
            serverData.IsAiAgentConnected.ShouldBeFalse();
        }

        [Fact]
        public void McpServerData_CanSetServerVersion()
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.ServerVersion = "1.2.3";

            // Assert
            serverData.ServerVersion.ShouldBe("1.2.3");
        }

        [Fact]
        public void McpServerData_CanSetServerApiVersion()
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.ServerApiVersion = "2.0.0";

            // Assert
            serverData.ServerApiVersion.ShouldBe("2.0.0");
        }

        [Theory]
        [InlineData(Consts.MCP.Server.TransportMethod.unknown)]
        [InlineData(Consts.MCP.Server.TransportMethod.stdio)]
        [InlineData(Consts.MCP.Server.TransportMethod.streamableHttp)]
        public void McpServerData_CanSetServerTransport(Consts.MCP.Server.TransportMethod transport)
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.ServerTransport = transport;

            // Assert
            serverData.ServerTransport.ShouldBe(transport);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void McpServerData_CanSetIsAiAgentConnected(bool isConnected)
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.IsAiAgentConnected = isConnected;

            // Assert
            serverData.IsAiAgentConnected.ShouldBe(isConnected);
        }

        [Fact]
        public void McpServerData_ObjectInitializer_ShouldSetAllProperties()
        {
            // Act
            var serverData = new McpServerData
            {
                ServerVersion = "1.0.0",
                ServerApiVersion = "1.0.0",
                ServerTransport = Consts.MCP.Server.TransportMethod.stdio,
                IsAiAgentConnected = true
            };

            // Assert
            serverData.ServerVersion.ShouldBe("1.0.0");
            serverData.ServerApiVersion.ShouldBe("1.0.0");
            serverData.ServerTransport.ShouldBe(Consts.MCP.Server.TransportMethod.stdio);
            serverData.IsAiAgentConnected.ShouldBeTrue();
        }

        [Fact]
        public void McpServerData_Serialize_ShouldUseJsonPropertyNames()
        {
            // Arrange
            var serverData = new McpServerData
            {
                ServerVersion = "1.0.0",
                ServerApiVersion = "2.0.0",
                ServerTransport = Consts.MCP.Server.TransportMethod.stdio,
                IsAiAgentConnected = true
            };

            // Act
            var json = JsonSerializer.Serialize(serverData);

            // Assert
            json.ShouldContain("\"serverVersion\":");
            json.ShouldContain("\"serverApiVersion\":");
            json.ShouldContain("\"serverTransport\":");
            json.ShouldContain("\"isAiAgentConnected\":");
        }

        [Theory]
        [InlineData(Consts.MCP.Server.TransportMethod.stdio, "stdio")]
        [InlineData(Consts.MCP.Server.TransportMethod.streamableHttp, "streamableHttp")]
        [InlineData(Consts.MCP.Server.TransportMethod.unknown, "unknown")]
        public void McpServerData_Serialize_ShouldSerializeTransportAsString(
            Consts.MCP.Server.TransportMethod transport,
            string expectedString)
        {
            // Arrange
            var serverData = new McpServerData
            {
                ServerTransport = transport
            };

            // Act
            var json = JsonSerializer.Serialize(serverData);

            // Assert
            json.ShouldContain($"\"serverTransport\":\"{expectedString}\"");
        }

        [Theory]
        [InlineData("stdio", Consts.MCP.Server.TransportMethod.stdio)]
        [InlineData("streamableHttp", Consts.MCP.Server.TransportMethod.streamableHttp)]
        [InlineData("unknown", Consts.MCP.Server.TransportMethod.unknown)]
        public void McpServerData_Deserialize_ShouldDeserializeTransportFromString(
            string transportString,
            Consts.MCP.Server.TransportMethod expectedTransport)
        {
            // Arrange
            var json = $"{{\"serverTransport\":\"{transportString}\"}}";

            // Act
            var serverData = JsonSerializer.Deserialize<McpServerData>(json);

            // Assert
            serverData.ShouldNotBeNull();
            serverData!.ServerTransport.ShouldBe(expectedTransport);
        }

        [Fact]
        public void McpServerData_RoundTrip_ShouldPreserveAllValues()
        {
            // Arrange
            var original = new McpServerData
            {
                ServerVersion = "1.2.3",
                ServerApiVersion = "1.0.0",
                ServerTransport = Consts.MCP.Server.TransportMethod.streamableHttp,
                IsAiAgentConnected = true
            };

            // Act
            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<McpServerData>(json);

            // Assert
            deserialized.ShouldNotBeNull();
            deserialized!.ServerVersion.ShouldBe(original.ServerVersion);
            deserialized.ServerApiVersion.ShouldBe(original.ServerApiVersion);
            deserialized.ServerTransport.ShouldBe(original.ServerTransport);
            deserialized.IsAiAgentConnected.ShouldBe(original.IsAiAgentConnected);
        }
    }
}
