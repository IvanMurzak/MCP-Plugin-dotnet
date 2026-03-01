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
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class McpStrategyFactoryTests
    {
        private readonly McpStrategyFactory _factory = new();

        [Fact]
        public void Create_None_ReturnsNoAuthMcpStrategy()
        {
            // Act
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.none);

            // Assert
            strategy.ShouldBeOfType<NoAuthMcpStrategy>();
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.none);
        }

        [Fact]
        public void Create_Required_ReturnsRequiredAuthMcpStrategy()
        {
            // Act
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.required);

            // Assert
            strategy.ShouldBeOfType<RequiredAuthMcpStrategy>();
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.required);
        }

        [Fact]
        public void Create_Unknown_ThrowsArgumentException()
        {
            // Act
            Action act = () => _factory.Create(Consts.MCP.Server.AuthOption.unknown);

            // Assert
            Should.Throw<ArgumentException>(act)
                .Message.ShouldContain("Unsupported auth option");
        }
    }
}
