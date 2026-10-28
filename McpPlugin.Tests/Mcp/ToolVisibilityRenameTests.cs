/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#pragma warning disable CS0618 // the old "Enabled" names are exercised on purpose
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// The per-tool flag was renamed from "Enabled" to "Visible": it only decides whether a tool is listed in
    /// tools/list. The old names stay as obsolete aliases, and a hidden tool still runs through tools/call.
    /// </summary>
    [Collection("McpPlugin")]
    public class ToolVisibilityRenameTests
    {
        private readonly Version _version = new Version();

        private IToolManager BuildManager()
        {
            var builder = new McpPluginBuilder(_version);
            builder.WithTool("visible-tool", "Visible", typeof(TestToolClass), typeof(TestToolClass).GetMethod(nameof(TestToolClass.First))!);
            builder.WithTool("hidden-tool", "Hidden", typeof(TestToolClass), typeof(TestToolClass).GetMethod(nameof(TestToolClass.Second))!);
            return builder.Build(new Reflector()).McpManager.ToolManager!;
        }

        [Fact]
        public async Task HiddenTool_IsLeftOutOfTheVisibleCount_ButStillRuns()
        {
            var toolManager = BuildManager();
            toolManager.SetToolVisible("hidden-tool", false).ShouldBeTrue();

            toolManager.IsToolVisible("hidden-tool").ShouldBeFalse();
            toolManager.VisibleToolsCount.ShouldBe(1);

            var response = await toolManager.RunCallTool(new RequestCallTool("hidden-tool", new Dictionary<string, JsonElement>()));
            response.Status.ShouldBe(ResponseStatus.Success);
            response.Value!.Status.ShouldBe(ResponseStatus.Success);
        }

        [Fact]
        public void OldManagerNames_ForwardToTheNewOnes_ThroughTheInterface()
        {
            var toolManager = BuildManager();

            toolManager.SetToolEnabled("hidden-tool", false).ShouldBeTrue();
            toolManager.IsToolVisible("hidden-tool").ShouldBeFalse();
            toolManager.IsToolEnabled("hidden-tool").ShouldBeFalse();
            toolManager.EnabledToolsCount.ShouldBe(toolManager.VisibleToolsCount);
            toolManager.EnabledToolsTokenCount.ShouldBe(toolManager.VisibleToolsTokenCount);
            toolManager.EnabledToolsSkillMetadataTokenCount.ShouldBe(toolManager.VisibleToolsSkillMetadataTokenCount);

            toolManager.SetToolEnabled("missing-tool", false).ShouldBeFalse();
        }

        [Fact]
        public void OldManagerNames_ForwardToTheNewOnes_OnTheConcreteManager()
        {
            var toolManager = (McpToolManager)BuildManager();

            toolManager.SetToolEnabled("hidden-tool", false).ShouldBeTrue();
            toolManager.IsToolVisible("hidden-tool").ShouldBeFalse();
            toolManager.IsToolEnabled("hidden-tool").ShouldBeFalse();
            toolManager.EnabledToolsCount.ShouldBe(1);
            toolManager.EnabledToolsTokenCount.ShouldBe(toolManager.VisibleToolsTokenCount);
        }

        [Fact]
        public void OldRunnerName_ForwardsToVisible()
        {
            var tool = BuildManager().GetAllTools().First(t => t.Name == "hidden-tool");

            tool.Enabled = false;
            tool.Visible.ShouldBeFalse();
            tool.Visible = true;
            tool.Enabled.ShouldBeTrue();

            var proxy = new ProxyTool("p", null, null, null, null, null, null, null, null, null, null,
                (_, _, _) => Task.FromResult(ResponseCallTool.Success("ok")));
            proxy.Enabled = false;
            proxy.Visible.ShouldBeFalse();
        }

        [Fact]
        public void OldInterfaceName_ForwardsToVisible_ForAnImplementationThatOnlyKnowsVisible()
        {
            IRunTool tool = new ReplayLikeTool();

            tool.Enabled = false;
            tool.Visible.ShouldBeFalse();
        }

        [Fact]
        public void OldAttributeName_SetsTheVisibleValue()
        {
            var attr = new AiToolAttribute("t") { Enabled = false };

            attr.Visible.ShouldBeFalse();
            attr.VisibleValue.ShouldBe(false);
            attr.EnabledValue.ShouldBe(false);
            new AiToolAttribute("t").EnabledValue.ShouldBeNull();
        }

        [Fact]
        public void OldDtoName_ForwardsToVisible()
        {
            var dto = new ResponseListTool { Enabled = false };
            dto.Visible.ShouldBeFalse();
        }

        private sealed class ReplayLikeTool : IRunTool
        {
            public bool Visible { get; set; } = true;
            public string Name => "replay-like";
            public string? Title => null;
            public string? Description => null;
            public string? SkillDescription => null;
            public string? SkillBody => null;
            public System.Text.Json.Nodes.JsonNode? InputSchema => null;
            public System.Text.Json.Nodes.JsonNode? OutputSchema => null;
            public bool? ReadOnlyHint => null;
            public bool? DestructiveHint => null;
            public bool? IdempotentHint => null;
            public bool? OpenWorldHint => null;
            public int TokenCount => 0;
            public Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseCallTool.Success("ok"));
        }

        private class TestToolClass
        {
            public string First() => "first";
            public string Second() => "second";
        }
    }
}
