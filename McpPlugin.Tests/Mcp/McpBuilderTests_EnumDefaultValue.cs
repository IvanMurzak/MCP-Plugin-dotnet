using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.McpPlugin.Utils;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    public enum TestEnum
    {
        Value1,
        Value2
    }

    public class Method_EnumDefaultValue
    {
        public string Process(TestEnum? options = TestEnum.Value2)
        {
            return options?.ToString() ?? "null";
        }
    }

    public class Method_MixedDefaultValue
    {
        public string Process(int count, TestEnum? options = TestEnum.Value2)
        {
            return $"{count}-{options?.ToString() ?? "null"}";
        }
    }

    [Collection("McpPlugin")]
    public class McpBuilderTests_EnumDefaultValue
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public McpBuilderTests_EnumDefaultValue(ITestOutputHelper output)
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
                .AddLogging(b => b.AddXunitTestOutput(_output));

            mcpPluginBuilder.WithTool(
                name: toolName,
                title: toolTitle,
                classType: classType,
                methodInfo: method);

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

        [Fact]
        public async Task CallTool_WithEnumDefaultValue_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_EnumDefaultValue), nameof(Method_EnumDefaultValue.Process));
            var toolName = typeof(Method_EnumDefaultValue).GetTypeShortName();

            // Act - calling without arguments, expecting default value TestEnum.Value2
            var request = new RequestCallTool(toolName, new Dictionary<string, JsonElement>());
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            if (response.Status != ResponseStatus.Success)
            {
                _output.WriteLine($"Error: {response.Message}");
            }
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("Value2");
        }

        [Fact]
        public async Task CallTool_WithMixedParameters_ShouldSucceed()
        {
            // Arrange
            var mcpPlugin = BuildMcpPluginWithTool(typeof(Method_MixedDefaultValue), nameof(Method_MixedDefaultValue.Process));
            var toolName = typeof(Method_MixedDefaultValue).GetTypeShortName();

            // Act - calling with only 'count', expecting default value TestEnum.Value2 for 'options'
            var request = new RequestCallTool(toolName, CreateArguments(("count", 42)));
            var response = await mcpPlugin.McpManager.ToolManager!.RunCallTool(request);

            // Assert
            response.Should().NotBeNull();
            if (response.Status != ResponseStatus.Success)
            {
                _output.WriteLine($"Error: {response.Message}");
            }
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();
            response.Value!.StructuredContent.Should().NotBeNull();
            response.Value!.StructuredContent!["result"]!.GetValue<string>().Should().Be("42-Value2");
        }
    }
}
