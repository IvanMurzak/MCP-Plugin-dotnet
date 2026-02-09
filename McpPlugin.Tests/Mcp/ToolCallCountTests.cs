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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ToolCallCountTests
    {
        private readonly Version _version = new Version();

        [Fact]
        public async Task ToolCallCount_ShouldIncrement_WhenToolIsCalled()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.TestMethod));
            
            mcpPluginBuilder.WithTool("testTool", "Test Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Verify initial state
            toolManager.ToolCallCount.Should().Be(0);
            plugin.ToolCallCount.Should().Be(0);

            // Act
            var request = new RequestCallTool("testTool", new Dictionary<string, JsonElement>());
            await toolManager.RunCallTool(request);

            // Assert
            toolManager.ToolCallCount.Should().Be(1);
            plugin.ToolCallCount.Should().Be(1);
        }

        [Fact]
        public async Task ToolCallCount_ShouldIncrementMultipleTimes_WhenToolIsCalledMultipleTimes()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.TestMethod));
            
            mcpPluginBuilder.WithTool("testTool", "Test Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act
            var request = new RequestCallTool("testTool", new Dictionary<string, JsonElement>());
            
            await toolManager.RunCallTool(request);
            await toolManager.RunCallTool(request);
            await toolManager.RunCallTool(request);

            // Assert
            toolManager.ToolCallCount.Should().Be(3);
            plugin.ToolCallCount.Should().Be(3);
        }

        [Fact]
        public async Task ToolCallCount_ShouldIncrement_ForDifferentTools()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method1 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.TestMethod));
            var method2 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.AnotherTestMethod));
            
            mcpPluginBuilder.WithTool("tool1", "Tool 1", typeof(TestToolClass), method1!);
            mcpPluginBuilder.WithTool("tool2", "Tool 2", typeof(TestToolClass), method2!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act
            var request1 = new RequestCallTool("tool1", new Dictionary<string, JsonElement>());
            var request2 = new RequestCallTool("tool2", new Dictionary<string, JsonElement>());
            
            await toolManager.RunCallTool(request1);
            await toolManager.RunCallTool(request2);
            await toolManager.RunCallTool(request1);

            // Assert - should count all calls regardless of which tool
            toolManager.ToolCallCount.Should().Be(3);
            plugin.ToolCallCount.Should().Be(3);
        }

        [Fact]
        public async Task ToolCallCount_ShouldIncrement_WhenToolNotFound()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act - call non-existent tool
            var request = new RequestCallTool("nonExistentTool", new Dictionary<string, JsonElement>());
            await toolManager.RunCallTool(request);

            // Assert - counter should still increment even if tool not found
            toolManager.ToolCallCount.Should().Be(1);
            plugin.ToolCallCount.Should().Be(1);
        }

        [Fact]
        public async Task ToolCallCount_ShouldBeThreadSafe()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.TestMethod));
            
            mcpPluginBuilder.WithTool("testTool", "Test Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act - simulate concurrent calls
            var tasks = new List<Task>();
            for (int i = 0; i < 100; i++)
            {
                var request = new RequestCallTool("testTool", new Dictionary<string, JsonElement>());
                tasks.Add(toolManager.RunCallTool(request));
            }
            
            await Task.WhenAll(tasks);

            // Assert
            toolManager.ToolCallCount.Should().Be(100);
            plugin.ToolCallCount.Should().Be(100);
        }

        private class TestToolClass
        {
            public string TestMethod() => "test result";
            public string AnotherTestMethod() => "another result";
        }
    }
}
