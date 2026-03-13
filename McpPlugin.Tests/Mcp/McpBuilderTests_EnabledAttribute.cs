/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpBuilderTests_EnabledAttribute
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public McpBuilderTests_EnabledAttribute(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private IMcpPlugin BuildWithTools()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(AnnotatedToolClass));
            return builder.Build(reflector);
        }

        private IMcpPlugin BuildWithPrompts()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithPrompts(typeof(AnnotatedPromptClass));
            return builder.Build(reflector);
        }

        // ── Tool: Enabled attribute ──────────────────────────────────────

        [Fact]
        public async Task Tool_DefaultEnabled_ShouldBeTrue()
        {
            var plugin = BuildWithTools();
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);

            var tool = response.Value!.First(t => t.Name == "tool-enabled-default");
            tool.Enabled.ShouldBeTrue();
        }

        [Fact]
        public async Task Tool_EnabledTrue_ShouldBeTrue()
        {
            var plugin = BuildWithTools();
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);

            var tool = response.Value!.First(t => t.Name == "tool-enabled-true");
            tool.Enabled.ShouldBeTrue();
        }

        [Fact]
        public async Task Tool_EnabledFalse_ShouldBeFalse()
        {
            var plugin = BuildWithTools();
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);

            var tool = response.Value!.First(t => t.Name == "tool-enabled-false");
            tool.Enabled.ShouldBeFalse();
        }

        [Fact]
        public void Tool_EnabledFalse_ShouldBeExcludedFromEnabledCount()
        {
            var plugin = BuildWithTools();
            var toolManager = plugin.McpManager.ToolManager!;
            var allTools = toolManager.GetAllTools().ToList();

            var disabledTool = allTools.First(t => t.Name == "tool-enabled-false");
            disabledTool.Enabled.ShouldBeFalse();

            toolManager.EnabledToolsCount.ShouldBe(allTools.Count - 1);
        }

        [Fact]
        public void Tool_RunnerEnabled_ShouldMatchAttribute()
        {
            var plugin = BuildWithTools();
            var tools = plugin.McpManager.ToolManager!.GetAllTools().ToList();

            var defaultTool = tools.First(t => t.Name == "tool-enabled-default");
            var enabledTool = tools.First(t => t.Name == "tool-enabled-true");
            var disabledTool = tools.First(t => t.Name == "tool-enabled-false");

            defaultTool.Enabled.ShouldBeTrue();
            enabledTool.Enabled.ShouldBeTrue();
            disabledTool.Enabled.ShouldBeFalse();
        }

        // ── Prompt: Enabled attribute ────────────────────────────────────

        [Fact]
        public void Prompt_DefaultEnabled_ShouldBeTrue()
        {
            var plugin = BuildWithPrompts();
            var prompts = plugin.McpManager.PromptManager!.GetAllPrompts().ToList();

            var prompt = prompts.First(p => p.Name == "prompt-enabled-default");
            prompt.Enabled.ShouldBeTrue();
        }

        [Fact]
        public void Prompt_EnabledTrue_ShouldBeTrue()
        {
            var plugin = BuildWithPrompts();
            var prompts = plugin.McpManager.PromptManager!.GetAllPrompts().ToList();

            var prompt = prompts.First(p => p.Name == "prompt-enabled-true");
            prompt.Enabled.ShouldBeTrue();
        }

        [Fact]
        public void Prompt_EnabledFalse_ShouldBeFalse()
        {
            var plugin = BuildWithPrompts();
            var prompts = plugin.McpManager.PromptManager!.GetAllPrompts().ToList();

            var prompt = prompts.First(p => p.Name == "prompt-enabled-false");
            prompt.Enabled.ShouldBeFalse();
        }

        [Fact]
        public void Prompt_EnabledFalse_ShouldBeExcludedFromEnabledCount()
        {
            var plugin = BuildWithPrompts();
            var promptManager = plugin.McpManager.PromptManager!;
            var allPrompts = promptManager.GetAllPrompts().ToList();

            promptManager.EnabledPromptsCount.ShouldBe(allPrompts.Count - 1);
        }

        // ── Attribute: EnabledValue nullable semantics ───────────────────

        [Fact]
        public void ToolAttribute_EnabledNotSet_EnabledValueShouldBeNull()
        {
            var attr = new McpPluginToolAttribute("test");
            attr.EnabledValue.ShouldBeNull();
        }

        [Fact]
        public void ToolAttribute_EnabledSetTrue_EnabledValueShouldBeTrue()
        {
            var attr = new McpPluginToolAttribute("test") { Enabled = true };
            attr.EnabledValue.ShouldBe(true);
        }

        [Fact]
        public void ToolAttribute_EnabledSetFalse_EnabledValueShouldBeFalse()
        {
            var attr = new McpPluginToolAttribute("test") { Enabled = false };
            attr.EnabledValue.ShouldBe(false);
        }

        [Fact]
        public void PromptAttribute_EnabledNotSet_EnabledValueShouldBeNull()
        {
            var attr = new McpPluginPromptAttribute();
            attr.EnabledValue.ShouldBeNull();
        }

        [Fact]
        public void PromptAttribute_EnabledSetFalse_EnabledValueShouldBeFalse()
        {
            var attr = new McpPluginPromptAttribute { Enabled = false };
            attr.EnabledValue.ShouldBe(false);
        }

        [Fact]
        public void ResourceAttribute_EnabledNotSet_EnabledValueShouldBeNull()
        {
            var attr = new McpPluginResourceAttribute();
            attr.EnabledValue.ShouldBeNull();
        }

        [Fact]
        public void ResourceAttribute_EnabledSetFalse_EnabledValueShouldBeFalse()
        {
            var attr = new McpPluginResourceAttribute { Enabled = false };
            attr.EnabledValue.ShouldBe(false);
        }
    }
}
