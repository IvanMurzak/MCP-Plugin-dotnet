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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Transport;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class TransportFactoryTests
    {
        private readonly TransportFactory _factory = new();

        [Fact]
        public void Create_Stdio_ReturnsStdioTransportLayer()
        {
            // Act
            var transport = _factory.Create(Consts.MCP.Server.TransportMethod.stdio);

            // Assert
            transport.Should().BeOfType<StdioTransportLayer>();
            transport.TransportMethod.Should().Be(Consts.MCP.Server.TransportMethod.stdio);
        }

        [Fact]
        public void Create_StreamableHttp_ReturnsStreamableHttpTransportLayer()
        {
            // Act
            var transport = _factory.Create(Consts.MCP.Server.TransportMethod.streamableHttp);

            // Assert
            transport.Should().BeOfType<StreamableHttpTransportLayer>();
            transport.TransportMethod.Should().Be(Consts.MCP.Server.TransportMethod.streamableHttp);
        }

        [Fact]
        public void Create_Unknown_ThrowsArgumentException()
        {
            // Act
            Action act = () => _factory.Create(Consts.MCP.Server.TransportMethod.unknown);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.Message.Should().Contain("Unsupported transport method");
        }
    }
}
