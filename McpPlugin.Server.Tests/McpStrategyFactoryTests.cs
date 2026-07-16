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
        public void Create_OAuth_ReturnsAccountMcpStrategy()
        {
            // Act
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.oauth);

            // Assert
            strategy.ShouldBeOfType<AccountMcpStrategy>();
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.oauth);
        }

        [Fact]
        public void Create_Token_ReturnsLocalTokenMcpStrategy()
        {
            // mcp-authorize g6: the offline shared-secret mode.
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.token);

            strategy.ShouldBeOfType<LocalTokenMcpStrategy>();
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.token);
        }

        [Fact]
        public void Create_Required_AliasesOntoLocalTokenMcpStrategy()
        {
            // mcp-authorize g6 back-compat: the deprecated `required` alias must NOT throw (that would
            // crash an un-migrated binary) and must NOT downgrade to anonymous — it maps onto the same
            // token-gated strategy so old `authorization=required` configs keep working token-gated.
            var strategy = _factory.Create(Consts.MCP.Server.AuthOption.required);

            strategy.ShouldBeOfType<LocalTokenMcpStrategy>();
            // The resolved strategy reports the target-state `token` option, not the legacy alias name.
            strategy.AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.token);
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
