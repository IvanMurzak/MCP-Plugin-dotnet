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
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpBuilderTests
    {
        private readonly Version _version = new Version();

        [Fact]
        public void Build_WithoutLogging_ShouldSucceed()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            plugin.Should().NotBeNull();
        }

        [Fact]
        public void Build_CalledTwice_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            mcpPluginBuilder.Build(reflector);

            // Act
            Action act = () => mcpPluginBuilder.Build(reflector);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        [Fact]
        public void WithTool_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            mcpPluginBuilder.Build(reflector);

            // Act
            Action act = () => mcpPluginBuilder.WithTool(typeof(McpBuilderTests), typeof(McpBuilderTests).GetMethods()[0]);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        [Fact]
        public void WithConfig_AfterBuild_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            mcpPluginBuilder.Build(reflector);

            // Act
            Action act = () => mcpPluginBuilder.WithConfig(c => { });

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("The builder has already been built.");
        }

        [Fact]
        public void WithTool_EmptyName_ShouldThrowArgumentException()
        {
            // Arrange
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestTool).GetMethod(nameof(TestTool.Method));

            // Act
            Action act = () => mcpPluginBuilder.WithTool(
                name: "",
                title: "title",
                classType: typeof(TestTool),
                method: method!);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage($"Tool name cannot be null or empty. Type: {typeof(TestTool).Name}, Method: {method!.Name}");
        }

        [Fact]
        public void AddTool_DuplicateName_ShouldThrowArgumentException()
        {
            // Arrange
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var runner = new MockRunTool();

            mcpPluginBuilder.AddTool("tool1", runner);

            // Act
            Action act = () => mcpPluginBuilder.AddTool("tool1", runner);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Tool with name 'tool1' already exists.");
        }

        [Fact]
        public void Build_WithLogging_ShouldLogMessage()
        {
            // Arrange
            var reflector = new Reflector();
            var logs = new List<string>();
            var loggerProvider = new MockLoggerProvider(logs);

            var mcpPluginBuilder = new McpPluginBuilder(_version)
                .AddLogging(builder => builder.AddProvider(loggerProvider).SetMinimumLevel(LogLevel.Trace));

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            logs.Should().Contain("McpPlugin Ctor.");
        }

        private class TestTool
        {
            public void Method() { }
        }

        private class MockRunTool : IRunTool
        {
            public string Name => "MockTool";
            public string Title => "Mock Tool Title";
            public string Description => "Mock Tool";
            public JsonNode InputSchema => JsonNode.Parse("{}")!;
            public JsonNode OutputSchema => JsonNode.Parse("{}")!;
            public bool Enabled { get; set; } = true;
            public Task<ResponseCallTool> Run(RequestCallTool request) => throw new NotImplementedException();
            public Task<ResponseCallTool> Run(string name, IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }

        private class MockLoggerProvider : ILoggerProvider
        {
            private readonly List<string> _logs;

            public MockLoggerProvider(List<string> logs)
            {
                _logs = logs;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new MockLogger(_logs);
            }

            public void Dispose() { }
        }

        private class MockLogger : ILogger
        {
            private readonly List<string> _logs;

            public MockLogger(List<string> logs)
            {
                _logs = logs;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _logs.Add(formatter(state, exception));
            }
        }

        [Fact]
        public void Build_ShouldHaveVersionProperty()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            plugin.Version.Should().NotBeNull();
            plugin.Version.Should().Be(_version);
        }

        [Fact]
        public void Build_ShouldHaveCurrentBaseDirectoryProperty()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            plugin.CurrentBaseDirectory.Should().NotBeNull();
            plugin.CurrentBaseDirectory.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Build_WhenNotConnected_ShouldHaveNullVersionHandshakeStatus()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            plugin.VersionHandshakeStatus.Should().BeNull();
        }

        [Fact]
        public void Build_RemoteMcpManagerHub_ShouldNotBeNull()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);

            // Act
            var plugin = mcpPluginBuilder.Build(reflector);

            // Assert
            plugin.RemoteMcpManagerHub.Should().NotBeNull();
        }

        [Fact]
        public void RemoteMcpManagerHub_ShouldHaveVersionHandshakeStatusProperty()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var plugin = mcpPluginBuilder.Build(reflector);

            // Act & Assert
            plugin.RemoteMcpManagerHub.Should().NotBeNull();
            plugin.RemoteMcpManagerHub.VersionHandshakeStatus.Should().BeNull();
        }
    }
}
