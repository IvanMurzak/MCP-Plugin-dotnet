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
using com.IvanMurzak.McpPlugin.Server.Strategy;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class McpStrategyFactoryTests
    {
        private readonly McpStrategyFactory _factory = new();

        [Fact]
        public void Create_Local_ReturnsLocalMcpStrategy()
        {
            // Act
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.none);

            // Assert
            strategy.Should().BeOfType<LocalMcpStrategy>();
            strategy.AuthOption.Should().Be(Consts.MCP.Server.AuthOption.none);
        }

        [Fact]
        public void Create_Remote_ReturnsRemoteMcpStrategy()
        {
            // Act
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.required);

            // Assert
            strategy.Should().BeOfType<RemoteMcpStrategy>();
            strategy.AuthOption.Should().Be(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void Create_Unknown_ThrowsArgumentException()
        {
            // Act
            Action act = () => _factory.Create(Consts.MCP.Server.AuthOption.unknown);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Which.Message.Should().Contain("Unsupported deployment mode");
        }
    }
}
