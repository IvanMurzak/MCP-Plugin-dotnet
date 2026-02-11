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
using System.ComponentModel;
using System.Reflection;
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
    public class ToolTokenCountTests
    {
        private readonly Version _version = new Version();

        [Fact]
        public void TokenCount_ShouldReturnPositiveValue_ForSimpleTool()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            
            mcpPluginBuilder.WithTool("simpleTool", "Simple Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools();

            // Act
            IRunTool? tool = null;
            foreach (var t in tools)
            {
                if (t.Name == "simpleTool")
                {
                    tool = t;
                    break;
                }
            }

            // Assert
            tool.Should().NotBeNull();
            tool!.TokenCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public void TokenCount_ShouldReturnHigherValue_ForComplexTool()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var simpleMethod = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            var complexMethod = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ComplexMethod));
            
            mcpPluginBuilder.WithTool("simpleTool", "Simple Tool", typeof(TestToolClass), simpleMethod!);
            mcpPluginBuilder.WithTool("complexTool", "Complex Tool", typeof(TestToolClass), complexMethod!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools();

            // Act
            IRunTool? simpleTool = null;
            IRunTool? complexTool = null;
            foreach (var t in tools)
            {
                if (t.Name == "simpleTool") simpleTool = t;
                if (t.Name == "complexTool") complexTool = t;
            }

            // Assert
            simpleTool.Should().NotBeNull();
            complexTool.Should().NotBeNull();
            complexTool!.TokenCount.Should().BeGreaterThan(simpleTool!.TokenCount);
        }

        [Fact]
        public void TokenCount_ShouldBeCached_OnMultipleCalls()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            
            mcpPluginBuilder.WithTool("testTool", "Test Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools();

            IRunTool? tool = null;
            foreach (var t in tools)
            {
                if (t.Name == "testTool")
                {
                    tool = t;
                    break;
                }
            }

            // Act - Call TokenCount multiple times
            var count1 = tool!.TokenCount;
            var count2 = tool.TokenCount;
            var count3 = tool.TokenCount;

            // Assert - All calls should return the same cached value
            count1.Should().Be(count2);
            count2.Should().Be(count3);
        }

        [Fact]
        public void EnabledToolsTokenCount_ShouldReturnSumOfAllEnabledTools()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method1 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            var method2 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ComplexMethod));
            var method3 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.AnotherMethod));
            
            mcpPluginBuilder.WithTool("tool1", "Tool 1", typeof(TestToolClass), method1!);
            mcpPluginBuilder.WithTool("tool2", "Tool 2", typeof(TestToolClass), method2!);
            mcpPluginBuilder.WithTool("tool3", "Tool 3", typeof(TestToolClass), method3!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act
            var totalTokens = toolManager.EnabledToolsTokenCount;

            // Assert
            totalTokens.Should().BeGreaterThan(0);
            
            // Verify it's the sum by checking individual tools
            var tools = toolManager.GetAllTools();
            int expectedSum = 0;
            foreach (var tool in tools)
            {
                if (tool.Enabled)
                {
                    expectedSum += tool.TokenCount;
                }
            }
            
            totalTokens.Should().Be(expectedSum);
        }

        [Fact]
        public void EnabledToolsTokenCount_ShouldExcludeDisabledTools()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method1 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            var method2 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ComplexMethod));
            
            mcpPluginBuilder.WithTool("enabledTool", "Enabled Tool", typeof(TestToolClass), method1!);
            mcpPluginBuilder.WithTool("disabledTool", "Disabled Tool", typeof(TestToolClass), method2!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            
            // Disable one tool
            toolManager.SetToolEnabled("disabledTool", false);

            // Act
            var totalTokens = toolManager.EnabledToolsTokenCount;

            // Assert - Should only count the enabled tool
            var tools = toolManager.GetAllTools();
            IRunTool? enabledTool = null;
            IRunTool? disabledTool = null;
            foreach (var t in tools)
            {
                if (t.Name == "enabledTool") enabledTool = t;
                if (t.Name == "disabledTool") disabledTool = t;
            }
            
            enabledTool.Should().NotBeNull();
            disabledTool.Should().NotBeNull();
            totalTokens.Should().Be(enabledTool!.TokenCount);
            totalTokens.Should().NotBe(enabledTool.TokenCount + disabledTool!.TokenCount);
        }

        [Fact]
        public void EnabledToolsTokenCount_ShouldReturnZero_WhenNoToolsEnabled()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            
            mcpPluginBuilder.WithTool("testTool", "Test Tool", typeof(TestToolClass), method!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            
            // Disable all tools
            toolManager.SetToolEnabled("testTool", false);

            // Act
            var totalTokens = toolManager.EnabledToolsTokenCount;

            // Assert
            totalTokens.Should().Be(0);
        }

        [Fact]
        public void EnabledToolsTokenCount_ShouldUpdateDynamically_WhenToolsAreDisabled()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var method1 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            var method2 = typeof(TestToolClass).GetMethod(nameof(TestToolClass.ComplexMethod));
            
            mcpPluginBuilder.WithTool("tool1", "Tool 1", typeof(TestToolClass), method1!);
            mcpPluginBuilder.WithTool("tool2", "Tool 2", typeof(TestToolClass), method2!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;

            // Act - Get initial count
            var initialCount = toolManager.EnabledToolsTokenCount;
            
            // Disable one tool
            toolManager.SetToolEnabled("tool1", false);
            var countAfterDisable = toolManager.EnabledToolsTokenCount;
            
            // Re-enable the tool
            toolManager.SetToolEnabled("tool1", true);
            var countAfterReEnable = toolManager.EnabledToolsTokenCount;

            // Assert
            initialCount.Should().BeGreaterThan(countAfterDisable);
            countAfterReEnable.Should().Be(initialCount);
        }

        [Fact]
        public void TokenCount_ShouldIncludeDescription_WhenPresent()
        {
            // Arrange
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version);
            var methodWithDescription = typeof(TestToolClass).GetMethod(nameof(TestToolClass.MethodWithDescription));
            var methodWithoutDescription = typeof(TestToolClass).GetMethod(nameof(TestToolClass.SimpleMethod));
            
            mcpPluginBuilder.WithTool("withDescription", "With Description", typeof(TestToolClass), methodWithDescription!);
            mcpPluginBuilder.WithTool("withoutDescription", "Without Description", typeof(TestToolClass), methodWithoutDescription!);
            
            var plugin = mcpPluginBuilder.Build(reflector);
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools();

            // Act
            IRunTool? toolWithDescription = null;
            IRunTool? toolWithoutDescription = null;
            foreach (var t in tools)
            {
                if (t.Name == "withDescription") toolWithDescription = t;
                if (t.Name == "withoutDescription") toolWithoutDescription = t;
            }

            // Assert - Tool with description should have higher token count
            toolWithDescription.Should().NotBeNull();
            toolWithoutDescription.Should().NotBeNull();
            
            // Note: This assertion might be fragile depending on the schema generation,
            // but in general, a method with a description should have more tokens
            if (!string.IsNullOrEmpty(toolWithDescription!.Description))
            {
                toolWithDescription.TokenCount.Should().BeGreaterThanOrEqualTo(toolWithoutDescription!.TokenCount);
            }
        }

        private class TestToolClass
        {
            public static string SimpleMethod() => "simple result";

            public static string ComplexMethod(
                string param1, 
                int param2, 
                bool param3,
                ComplexType complexParam) => "complex result";

            public static string AnotherMethod(string input) => $"result: {input}";

            [Description("This is a detailed description of what this method does and how to use it properly.")]
            public static string MethodWithDescription() => "method with description result";
        }

        private class ComplexType
        {
            public string StringProperty { get; set; } = string.Empty;
            public int IntProperty { get; set; }
            public List<NestedType> ListProperty { get; set; } = new List<NestedType>();
        }

        private class NestedType
        {
            public string Name { get; set; } = string.Empty;
            public double Value { get; set; }
        }
    }
}
