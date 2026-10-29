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
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [AiToolType(Target = ToolTarget.Runtime)]
    public static class GameOnlyToolClass
    {
        [AiTool("game-scene-tree")]
        public static string SceneTree() => "game";

        [AiTool("game-and-editor", Target = ToolTarget.Both)]
        public static string Shared() => "both";
    }

    [AiToolType]
    public static class MixedTargetToolClass
    {
        [AiTool("everywhere")]
        public static string Everywhere() => "both";

        [AiTool("editor-only", Target = ToolTarget.Editor)]
        public static string EditorOnly() => "editor";

        [AiTool("runtime-only", Target = ToolTarget.Runtime)]
        public static string RuntimeOnly() => "runtime";

        [AiTool("runtime-system", Target = ToolTarget.Runtime, ToolType = McpToolType.System)]
        public static string RuntimeSystem() => "runtime";
    }

    /// <summary>
    /// Wave-6 finding: a game-only [AiToolType] tool declared in the game assembly was also registered by the editor
    /// (same assembly scan). A tool whose target does not include the plugin's <see cref="ClientRole"/> is skipped.
    /// </summary>
    [Collection("McpPlugin")]
    public class McpBuilderTests_ToolTarget
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;

        public McpBuilderTests_ToolTarget(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        private IMcpPlugin Build(ClientRole? role, string environment = "Godot 4.5", bool viaSetConfig = true)
        {
            var builder = new McpPluginBuilder(new Version { Environment = environment }, _loggerProvider)
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(typeof(GameOnlyToolClass), typeof(MixedTargetToolClass));
            if (role is { } r)
            {
                if (viaSetConfig)
                    builder.SetConfig(new ConnectionConfig { ClientRole = r, GenerateSkillFiles = false });
                else
                    builder.WithConfig(config => { config.ClientRole = r; config.GenerateSkillFiles = false; });
            }
            return builder.Build(new Reflector());
        }

        private static string[] ToolNames(IMcpPlugin plugin)
            => plugin.McpManager.ToolManager!.GetAllTools().Select(t => t.Name).OrderBy(n => n).ToArray();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Editor_SkipsRuntimeTools(bool viaSetConfig)
        {
            using var plugin = Build(ClientRole.Editor, viaSetConfig: viaSetConfig);
            ToolNames(plugin).ShouldBe(new[] { "editor-only", "everywhere", "game-and-editor" });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Runtime_SkipsEditorTools(bool viaSetConfig)
        {
            using var plugin = Build(ClientRole.Runtime, viaSetConfig: viaSetConfig);
            ToolNames(plugin).ShouldBe(new[] { "everywhere", "game-and-editor", "game-scene-tree", "runtime-only" });
        }

        [Fact]
        public void DefaultRole_IsTheEditor()
        {
            using var plugin = Build(role: null);
            ToolNames(plugin).ShouldBe(new[] { "editor-only", "everywhere", "game-and-editor" });
        }

        [Fact]
        public void RuntimeEnvironmentSuffix_MakesTheRuntime()
        {
            // The Godot runtime that only bumps the package (no ClientRole) keeps its game tools.
            using var plugin = Build(role: null, environment: "Godot 4.5 (runtime)");
            ToolNames(plugin).ShouldContain("game-scene-tree");
            ToolNames(plugin).ShouldNotContain("editor-only");
        }

        [Fact]
        public void SystemTools_AreFilteredToo()
        {
            using var editor = Build(ClientRole.Editor);
            editor.McpManager.SystemToolManager!.GetAllTools().Select(t => t.Name).ShouldNotContain("runtime-system");
            using var runtime = Build(ClientRole.Runtime);
            runtime.McpManager.SystemToolManager!.GetAllTools().Select(t => t.Name).ShouldContain("runtime-system");
        }

        [Fact]
        public async Task ToolList_CarriesTheTarget_NullForBoth()
        {
            using var plugin = Build(ClientRole.Runtime);
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());
            var byName = response.Value!.ToDictionary(t => t.Name);

            byName["game-scene-tree"].Target.ShouldBe(ToolTarget.Runtime);
            byName["runtime-only"].Target.ShouldBe(ToolTarget.Runtime);
            byName["game-and-editor"].Target.ShouldBeNull();
            byName["everywhere"].Target.ShouldBeNull();
        }

        [Fact]
        public void ToolTarget_RoundTripsOnTheWire()
        {
            var hub = new Microsoft.AspNetCore.SignalR.JsonHubProtocolOptions();
            SignalR_JsonConfiguration.ConfigureJsonSerializer(new Reflector(), hub);
            var options = hub.PayloadSerializerOptions;
            var json = JsonSerializer.Serialize(new ResponseListTool { Name = "t", Target = ToolTarget.Runtime, InputSchema = JsonDocument.Parse("{}").RootElement }, options);
            JsonSerializer.Deserialize<ResponseListTool>(json, options)!.Target.ShouldBe(ToolTarget.Runtime);
            JsonSerializer.Deserialize<ResponseListTool>("{\"name\":\"t\"}", options)!.Target.ShouldBeNull("an older plugin sends no target");
        }

        [Theory]
        [InlineData(ToolTarget.Both, ClientRole.Editor, true)]
        [InlineData(ToolTarget.Both, ClientRole.Runtime, true)]
        [InlineData(ToolTarget.Editor, ClientRole.Runtime, false)]
        [InlineData(ToolTarget.Runtime, ClientRole.Editor, false)]
        [InlineData((ToolTarget)0, ClientRole.Runtime, true)]
        public void Includes(ToolTarget target, ClientRole role, bool expected)
            => target.Includes(role).ShouldBe(expected);
    }
}
