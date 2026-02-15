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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class LocalMcpStrategyTests
    {
        private readonly LocalMcpStrategy _strategy = new();

        [Fact]
        public void DeploymentMode_ReturnsLocal()
        {
            _strategy.DeploymentMode.Should().Be(Consts.MCP.Server.DeploymentMode.local);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsFalse()
        {
            _strategy.AllowMultipleConnections.Should().BeFalse();
        }

        [Fact]
        public void Validate_WithoutToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);

            // Act & Assert
            var act = () => _strategy.Validate(dataArguments);
            act.Should().NotThrow();
        }

        [Fact]
        public void Validate_WithToken_DoesNotThrow()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });

            // Act & Assert
            var act = () => _strategy.Validate(dataArguments);
            act.Should().NotThrow();
        }

        [Fact]
        public void ConfigureAuthentication_WithoutToken_DoesNotRequireToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert
            options.RequireToken.Should().BeFalse();
            options.ServerToken.Should().BeNull();
        }

        [Fact]
        public void ConfigureAuthentication_WithToken_RequiresToken()
        {
            // Arrange
            var dataArguments = new DataArguments(new[] { "token=test-token" });
            var options = new TokenAuthenticationOptions();

            // Act
            _strategy.ConfigureAuthentication(options, dataArguments);

            // Assert
            options.RequireToken.Should().BeTrue();
            options.ServerToken.Should().Be("test-token");
        }
    }
}
