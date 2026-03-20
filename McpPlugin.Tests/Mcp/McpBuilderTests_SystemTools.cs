/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public class McpBuilderTests_SystemTools
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();

        public McpBuilderTests_SystemTools(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private IMcpPlugin BuildWithMixedTools()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(MixedToolTypeClass));
            return builder.Build(reflector);
        }

        // ── ToolType attribute defaults ──────────────────────────────────

        [Fact]
        public void ToolAttribute_DefaultToolType_ShouldBeStandard()
        {
            var attr = new McpPluginToolAttribute("test");
            attr.ToolType.ShouldBe(McpToolType.Standard);
        }

        [Fact]
        public void ToolAttribute_ToolTypeSystem_ShouldBeSystem()
        {
            var attr = new McpPluginToolAttribute("test") { ToolType = McpToolType.System };
            attr.ToolType.ShouldBe(McpToolType.System);
        }

        // ── Builder splits standard vs system tools ─────────────────────

        [Fact]
        public void Build_StandardTools_ShouldBeInToolManager()
        {
            var plugin = BuildWithMixedTools();
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools().Select(t => t.Name).ToList();

            tools.ShouldContain("standard-tool-a");
            tools.ShouldContain("standard-tool-b");
            tools.ShouldContain("standard-default");
        }

        [Fact]
        public void Build_SystemTools_ShouldNotBeInToolManager()
        {
            var plugin = BuildWithMixedTools();
            var toolManager = plugin.McpManager.ToolManager!;
            var tools = toolManager.GetAllTools().Select(t => t.Name).ToList();

            tools.ShouldNotContain("system-tool-x");
            tools.ShouldNotContain("system-tool-y");
        }

        [Fact]
        public void Build_SystemTools_ShouldBeInSystemToolManager()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            systemToolManager.ShouldNotBeNull();
            systemToolManager.HasTool("system-tool-x").ShouldBeTrue();
            systemToolManager.HasTool("system-tool-y").ShouldBeTrue();
        }

        [Fact]
        public void Build_StandardTools_ShouldNotBeInSystemToolManager()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            systemToolManager.HasTool("standard-tool-a").ShouldBeFalse();
            systemToolManager.HasTool("standard-tool-b").ShouldBeFalse();
            systemToolManager.HasTool("standard-default").ShouldBeFalse();
        }

        [Fact]
        public void Build_ToolCounts_ShouldBeCorrect()
        {
            var plugin = BuildWithMixedTools();
            var toolManager = plugin.McpManager.ToolManager!;
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            toolManager.TotalToolsCount.ShouldBe(3); // standard-tool-a, standard-tool-b, standard-default
            systemToolManager.TotalToolsCount.ShouldBe(2); // system-tool-x, system-tool-y
        }

        // ── System tool execution ───────────────────────────────────────

        [Fact]
        public async Task RunSystemTool_ExistingTool_ShouldSucceed()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            var request = new RequestCallTool("system-tool-x", new Dictionary<string, JsonElement>());
            var response = await systemToolManager.RunSystemTool(request);

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);
        }

        [Fact]
        public async Task RunSystemTool_NonExistentTool_ShouldReturnError()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            var request = new RequestCallTool("nonexistent-tool", new Dictionary<string, JsonElement>());
            var response = await systemToolManager.RunSystemTool(request);

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message!.ShouldContain("not found");
        }

        [Fact]
        public async Task RunSystemTool_NullRequest_ShouldReturnError()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            var response = await systemToolManager.RunSystemTool(null!);

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task RunSystemTool_EmptyName_ShouldReturnError()
        {
            var plugin = BuildWithMixedTools();
            var systemToolManager = plugin.McpManager.SystemToolManager!;

            var request = new RequestCallTool("", new Dictionary<string, JsonElement>());
            var response = await systemToolManager.RunSystemTool(request);

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Error);
            response.Message!.ShouldContain("empty");
        }

        // ── Standard tool listing excludes system tools ─────────────────

        [Fact]
        public async Task ListTools_ShouldOnlyReturnStandardTools()
        {
            var plugin = BuildWithMixedTools();
            var toolManager = plugin.McpManager.ToolManager!;

            var response = await toolManager.RunListTool(new RequestListTool());

            response.ShouldNotBeNull();
            response.Status.ShouldBe(ResponseStatus.Success);

            var names = response.Value!.Select(t => t.Name).ToList();
            names.ShouldContain("standard-tool-a");
            names.ShouldContain("standard-tool-b");
            names.ShouldContain("standard-default");
            names.ShouldNotContain("system-tool-x");
            names.ShouldNotContain("system-tool-y");
        }

        // ── SystemToolManager available when no system tools registered ─

        [Fact]
        public void Build_NoSystemTools_SystemToolManagerShouldStillExist()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(AnnotatedToolClass));
            var plugin = builder.Build(reflector);

            plugin.McpManager.SystemToolManager.ShouldNotBeNull();
            plugin.McpManager.SystemToolManager!.TotalToolsCount.ShouldBe(0);
        }

    }
}
