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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class RemoteMcpStrategyTests
    {
        private readonly RemoteMcpStrategy _strategy = new();

        [Fact]
        public void DeploymentMode_ReturnsRemote()
        {
            _strategy.DeploymentMode.Should().Be(Consts.MCP.Server.DeploymentMode.remote);
        }

        [Fact]
        public void AllowMultipleConnections_ReturnsTrue()
        {
            _strategy.AllowMultipleConnections.Should().BeTrue();
        }

        [Fact]
        public void Validate_WithoutToken_ThrowsInvalidOperationException()
        {
            // Arrange
            var dataArguments = new DataArguments(new string[0]);

            // Act & Assert
            var act = () => _strategy.Validate(dataArguments);
            act.Should().Throw<InvalidOperationException>()
                .Which.Message.Should().Contain("REMOTE deployment mode requires a token");
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
        public void ConfigureAuthentication_AlwaysRequiresToken()
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
