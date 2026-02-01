/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using FluentAssertions;
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
            serverData.ServerVersion.Should().BeNull();
            serverData.ServerApiVersion.Should().BeNull();
            serverData.ServerTransport.Should().Be(Consts.MCP.Server.TransportMethod.unknown);
            serverData.IsAiAgentConnected.Should().BeFalse();
        }

        [Fact]
        public void McpServerData_CanSetServerVersion()
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.ServerVersion = "1.2.3";

            // Assert
            serverData.ServerVersion.Should().Be("1.2.3");
        }

        [Fact]
        public void McpServerData_CanSetServerApiVersion()
        {
            // Arrange
            var serverData = new McpServerData();

            // Act
            serverData.ServerApiVersion = "2.0.0";

            // Assert
            serverData.ServerApiVersion.Should().Be("2.0.0");
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
            serverData.ServerTransport.Should().Be(transport);
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
            serverData.IsAiAgentConnected.Should().Be(isConnected);
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
            serverData.ServerVersion.Should().Be("1.0.0");
            serverData.ServerApiVersion.Should().Be("1.0.0");
            serverData.ServerTransport.Should().Be(Consts.MCP.Server.TransportMethod.stdio);
            serverData.IsAiAgentConnected.Should().BeTrue();
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
            json.Should().Contain("\"serverVersion\":");
            json.Should().Contain("\"serverApiVersion\":");
            json.Should().Contain("\"serverTransport\":");
            json.Should().Contain("\"isAiAgentConnected\":");
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
            json.Should().Contain($"\"serverTransport\":\"{expectedString}\"");
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
            serverData.Should().NotBeNull();
            serverData!.ServerTransport.Should().Be(expectedTransport);
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
            deserialized.Should().NotBeNull();
            deserialized!.ServerVersion.Should().Be(original.ServerVersion);
            deserialized.ServerApiVersion.Should().Be(original.ServerApiVersion);
            deserialized.ServerTransport.Should().Be(original.ServerTransport);
            deserialized.IsAiAgentConnected.Should().Be(original.IsAiAgentConnected);
        }
    }
}
