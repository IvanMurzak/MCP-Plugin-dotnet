/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpBuilderTests_ToolAnnotations
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version version = new Version();

        public McpBuilderTests_ToolAnnotations(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private async Task<ResponseListTool> GetTool(string toolName)
        {
            var reflector = new Reflector();
            var mcpPluginBuilder = new McpPluginBuilder(version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(AnnotatedToolClass));

            var mcpPlugin = mcpPluginBuilder.Build(reflector);
            var request = new RequestListTool();
            var response = await mcpPlugin.McpManager.ToolManager!.RunListTool(request);

            response.Should().NotBeNull();
            response.Status.Should().Be(ResponseStatus.Success);
            response.Value.Should().NotBeNull();

            var tool = System.Linq.Enumerable.FirstOrDefault(response.Value!, t => t.Name == toolName);
            tool.Should().NotBeNull($"Tool '{toolName}' should be registered");
            return tool!;
        }

        [Fact]
        public async Task ToolAnnotations_NoHints_ShouldHaveNullHints()
        {
            var tool = await GetTool("tool-no-hints");

            tool.ReadOnlyHint.Should().BeNull();
            tool.DestructiveHint.Should().BeNull();
            tool.IdempotentHint.Should().BeNull();
            tool.OpenWorldHint.Should().BeNull();
        }

        [Fact]
        public async Task ToolAnnotations_ReadOnlyHint_ShouldBeTrue()
        {
            var tool = await GetTool("tool-readonly");

            tool.ReadOnlyHint.Should().BeTrue();
            tool.DestructiveHint.Should().BeNull();
            tool.IdempotentHint.Should().BeNull();
            tool.OpenWorldHint.Should().BeNull();
        }

        [Fact]
        public async Task ToolAnnotations_DestructiveHintFalse_ShouldBeFalse()
        {
            var tool = await GetTool("tool-destructive-false");

            tool.ReadOnlyHint.Should().BeNull();
            tool.DestructiveHint.Should().BeFalse();
            tool.IdempotentHint.Should().BeNull();
            tool.OpenWorldHint.Should().BeNull();
        }

        [Fact]
        public async Task ToolAnnotations_IdempotentHint_ShouldBeTrue()
        {
            var tool = await GetTool("tool-idempotent");

            tool.ReadOnlyHint.Should().BeNull();
            tool.DestructiveHint.Should().BeNull();
            tool.IdempotentHint.Should().BeTrue();
            tool.OpenWorldHint.Should().BeNull();
        }

        [Fact]
        public async Task ToolAnnotations_OpenWorldHint_ShouldBeTrue()
        {
            var tool = await GetTool("tool-open-world");

            tool.ReadOnlyHint.Should().BeNull();
            tool.DestructiveHint.Should().BeNull();
            tool.IdempotentHint.Should().BeNull();
            tool.OpenWorldHint.Should().BeTrue();
        }

        [Fact]
        public async Task ToolAnnotations_AllHints_ShouldBeCorrect()
        {
            var tool = await GetTool("tool-all-hints");

            tool.ReadOnlyHint.Should().BeTrue();
            tool.DestructiveHint.Should().BeFalse();
            tool.IdempotentHint.Should().BeTrue();
            tool.OpenWorldHint.Should().BeFalse();
        }
    }
}
