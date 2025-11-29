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
using com.IvanMurzak.McpPlugin.Tests.Data.Other;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    #region Test Data Classes for Case-Insensitive Tests
    
    /// <summary>
    /// Method with no arguments for testing empty parameter case.
    /// </summary>
    public class Method_NoArgs_StringReturn
    {
        public string GetHello() => "Hello";
    }

    /// <summary>
    /// Method with parameter name containing underscore.
    /// </summary>
    public class Method_UnderscoreParam
    {
        public string Format(string user_name) => $"User: {user_name}";
    }

    /// <summary>
    /// Method with parameter name containing numbers.
    /// </summary>
    public class Method_NumberInParam
    {
        public int AddValue1(int value1) => value1 + 1;
    }

    /// <summary>
    /// Method with multiple parameters of same type.
    /// </summary>
    public class Method_ThreeParams
    {
        public string Combine(string first, string second, string third) => $"{first}-{second}-{third}";
    }

    /// <summary>
    /// Method with PascalCase parameter name (as sometimes used in .NET).
    /// </summary>
    public class Method_PascalCaseParam
    {
        public string Process(string InputValue) => InputValue.ToUpper();
    }

    /// <summary>
    /// Method with mixed parameter naming styles.
    /// </summary>
    public class Method_MixedParamStyles
    {
        public string Mix(string camelCase, string PascalCase, string snake_case) 
            => $"{camelCase}|{PascalCase}|{snake_case}";
    }

    /// <summary>
    /// Method with optional parameters.
    /// </summary>
    public class Method_OptionalParams
    {
        public int Calculate(int required, int optional = 10) => required + optional;
    }

    #endregion

    /// <summary>
    /// Tests for case-insensitive parameter name matching in MCP tool calls.
    /// Validates that LLM-provided input arguments with different casing are correctly matched
    /// to method parameters when there is no naming conflict.
    /// </summary>
    public class McpBuilderTests_CallTool_CaseInsensitive
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public McpBuilderTests_CallTool_CaseInsensitive(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private IMcpPlugin BuildMcpPluginWithTool(Type classType, string methodName)
        {
            var method = classType.GetMethod(methodName)!;
            var toolName = classType.GetTypeShortName();
            var toolTitle = $"Title of {toolName}";

            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .AddMcpManager();

            mcpPluginBuilder.WithTool(
                name: toolName,
                title: toolTitle,
                classType: classType,
                method: method);

            return mcpPluginBuilder.Build(reflector)!;
        }

        private static Dictionary<string, JsonElement> CreateArguments(params (string name, object value)[] args)
        {
            var dict = new Dictionary<string, JsonElement>();
            foreach (var (name, value) in args)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(value);
                dict[name] = JsonDocument.Parse(json).RootElement.Clone();
            }
            return dict;
        }

        #region Basic Case-Insensitive Matching Tests

        [Fact]
        public async Task CallTool_WithExactCaseParameter_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act - using exact case "value" (method parameter is "value")
            var request = new RequestCallTool(toolName, CreateArguments(("value", 5)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(6);
        }

        [Fact]
        public async Task CallTool_WithUpperCaseParameter_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act - using uppercase "VALUE" (method parameter is "value")
            var request = new RequestCallTool(toolName, CreateArguments(("VALUE", 10)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(11);
        }

        [Fact]
        public async Task CallTool_WithPascalCaseParameter_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act - using PascalCase "Value" (method parameter is "value")
            var request = new RequestCallTool(toolName, CreateArguments(("Value", 20)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(21);
        }

        [Fact]
        public async Task CallTool_WithMixedCaseParameters_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_TwoArgs_StringReturn), nameof(Method_TwoArgs_StringReturn.Concat));
            var toolName = typeof(Method_TwoArgs_StringReturn).GetTypeShortName();

            // Act - using mixed case "LEFT" and "Right" (method parameters are "left" and "right")
            var request = new RequestCallTool(toolName, CreateArguments(("LEFT", "Hello"), ("Right", "World")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("HelloWorld");
        }

        [Fact]
        public async Task CallTool_WithRandomCaseParameter_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act - using random case "vALUE" (method parameter is "value")
            var request = new RequestCallTool(toolName, CreateArguments(("vALUE", 30)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(31);
        }

        #endregion

        #region Edge Cases Tests

        [Fact]
        public async Task CallTool_WithNoParameters_ShouldSucceed()
        {
            // Arrange - Method takes no parameters
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_NoArgs_StringReturn), nameof(Method_NoArgs_StringReturn.GetHello));
            var toolName = typeof(Method_NoArgs_StringReturn).GetTypeShortName();

            // Act - Empty arguments
            var request = new RequestCallTool(toolName, CreateArguments());
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("Hello");
        }

        [Fact]
        public async Task CallTool_WithUnknownParameter_ShouldStillWork()
        {
            // Arrange - The unknown parameter should be passed through as-is
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act - using "value" (correct) plus an unknown parameter "unknown"
            var request = new RequestCallTool(toolName, CreateArguments(("value", 40), ("unknown", 99)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert - Should succeed (unknown parameters are typically ignored)
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(41);
        }

        [Fact]
        public async Task CallTool_WithUnderscoreParameterName_CaseInsensitive_ShouldSucceed()
        {
            // Arrange - Parameter name contains underscore: "user_name"
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_UnderscoreParam), nameof(Method_UnderscoreParam.Format));
            var toolName = typeof(Method_UnderscoreParam).GetTypeShortName();

            // Act - using "USER_NAME" (method parameter is "user_name")
            var request = new RequestCallTool(toolName, CreateArguments(("USER_NAME", "John")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("User: John");
        }

        [Fact]
        public async Task CallTool_WithNumberInParameterName_CaseInsensitive_ShouldSucceed()
        {
            // Arrange - Parameter name contains number: "value1"
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_NumberInParam), nameof(Method_NumberInParam.AddValue1));
            var toolName = typeof(Method_NumberInParam).GetTypeShortName();

            // Act - using "VALUE1" (method parameter is "value1")
            var request = new RequestCallTool(toolName, CreateArguments(("VALUE1", 100)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(101);
        }

        #endregion

        #region Multiple Parameters Tests

        [Fact]
        public async Task CallTool_WithThreeParameters_AllDifferentCases_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_ThreeParams), nameof(Method_ThreeParams.Combine));
            var toolName = typeof(Method_ThreeParams).GetTypeShortName();

            // Act - using "FIRST", "Second", "tHiRd" (method parameters are "first", "second", "third")
            var request = new RequestCallTool(toolName, CreateArguments(
                ("FIRST", "A"),
                ("Second", "B"),
                ("tHiRd", "C")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("A-B-C");
        }

        [Fact]
        public async Task CallTool_WithMixedParamStyles_CaseInsensitive_ShouldSucceed()
        {
            // Arrange - Method has params: camelCase, PascalCase, snake_case
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_MixedParamStyles), nameof(Method_MixedParamStyles.Mix));
            var toolName = typeof(Method_MixedParamStyles).GetTypeShortName();

            // Act - using all uppercase versions
            var request = new RequestCallTool(toolName, CreateArguments(
                ("CAMELCASE", "a"),
                ("PASCALCASE", "b"),
                ("SNAKE_CASE", "c")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("a|b|c");
        }

        #endregion

        #region PascalCase Method Parameter Tests

        [Fact]
        public async Task CallTool_PascalCaseMethodParam_WithLowerCase_ShouldSucceed()
        {
            // Arrange - Method parameter is "InputValue" (PascalCase)
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_PascalCaseParam), nameof(Method_PascalCaseParam.Process));
            var toolName = typeof(Method_PascalCaseParam).GetTypeShortName();

            // Act - using lowercase "inputvalue"
            var request = new RequestCallTool(toolName, CreateArguments(("inputvalue", "test")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("TEST");
        }

        [Fact]
        public async Task CallTool_PascalCaseMethodParam_WithCamelCase_ShouldSucceed()
        {
            // Arrange - Method parameter is "InputValue" (PascalCase)
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_PascalCaseParam), nameof(Method_PascalCaseParam.Process));
            var toolName = typeof(Method_PascalCaseParam).GetTypeShortName();

            // Act - using camelCase "inputValue"
            var request = new RequestCallTool(toolName, CreateArguments(("inputValue", "hello")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("HELLO");
        }

        #endregion

        #region Optional Parameters Tests

        [Fact]
        public async Task CallTool_WithOptionalParam_OnlyRequiredProvided_DifferentCase_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OptionalParams), nameof(Method_OptionalParams.Calculate));
            var toolName = typeof(Method_OptionalParams).GetTypeShortName();

            // Act - Only provide "REQUIRED" (method param is "required"), omit optional
            var request = new RequestCallTool(toolName, CreateArguments(("REQUIRED", 5)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert - Should use default value 10 for optional parameter
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(15); // 5 + 10 (default)
        }

        [Fact]
        public async Task CallTool_WithOptionalParam_BothProvided_DifferentCases_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OptionalParams), nameof(Method_OptionalParams.Calculate));
            var toolName = typeof(Method_OptionalParams).GetTypeShortName();

            // Act - Provide both "Required" and "OPTIONAL" with different casing
            var request = new RequestCallTool(toolName, CreateArguments(("Required", 7), ("OPTIONAL", 3)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(10); // 7 + 3
        }

        #endregion

        #region Typical LLM Behavior Tests

        [Theory]
        [InlineData("value", 1)]
        [InlineData("Value", 2)]
        [InlineData("VALUE", 3)]
        [InlineData("vAlUe", 4)]
        public async Task CallTool_WithVariousCasings_ShouldAllSucceed(string paramName, int inputValue)
        {
            // Arrange - LLMs often provide parameters in different casings
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_OneArg_IntReturn), nameof(Method_OneArg_IntReturn.AddOne));
            var toolName = typeof(Method_OneArg_IntReturn).GetTypeShortName();

            // Act
            var request = new RequestCallTool(toolName, CreateArguments((paramName, inputValue)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success, $"Parameter name '{paramName}' should work");
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<int>().Should().Be(inputValue + 1);
        }

        [Theory]
        [InlineData("left", "right", "AB")]
        [InlineData("LEFT", "RIGHT", "AB")]
        [InlineData("Left", "Right", "AB")]
        [InlineData("lEfT", "rIgHt", "AB")]
        public async Task CallTool_TwoParams_WithVariousCasings_ShouldAllSucceed(string leftParam, string rightParam, string expected)
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_TwoArgs_StringReturn), nameof(Method_TwoArgs_StringReturn.Concat));
            var toolName = typeof(Method_TwoArgs_StringReturn).GetTypeShortName();

            // Act
            var request = new RequestCallTool(toolName, CreateArguments((leftParam, "A"), (rightParam, "B")));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success, $"Parameters '{leftParam}' and '{rightParam}' should work");
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be(expected);
        }

        #endregion
    }
}
