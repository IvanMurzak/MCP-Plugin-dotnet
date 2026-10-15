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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ToolBindingTests
    {
        const string Tool = "scale";

        public sealed class Size { public int Width { get; set; } public int Height { get; set; } }

        public static class ScaleTool
        {
            public static Size Scale(Size size, int factor = 2) => new Size { Width = size.Width * factor, Height = size.Height * factor };
        }

        // Arguments arrive as "WxH" text; results leave as "WxH" text: no ReflectorNet involved.
        sealed class Binding : IToolBinding
        {
            public ToolSignature? Describe(string toolName, MethodInfo method, string? description) =>
                new ToolSignature("bound: " + description, JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"size\":{\"type\":\"string\"}}}")!);

            public bool TryConvert(MethodInfo method, ParameterInfo parameter, JsonElement value, out object? converted)
            {
                converted = null;
                if (parameter.ParameterType != typeof(Size)) return false;
                var parts = (value.GetString() ?? "").Split('x');
                if (parts.Length != 2) throw new ArgumentException("size must be WxH");
                converted = new Size { Width = int.Parse(parts[0]), Height = int.Parse(parts[1]) };
                return true;
            }

            public ResponseCallTool? Result(MethodInfo method, object? value) =>
                value is Size s ? ResponseCallTool.Success($"{s.Width}x{s.Height}") : null;
        }

        static McpToolManager Manager(IToolBinding? binding = null)
        {
            var reflector = new Reflector();
            var tools = new ToolRunnerCollection(reflector, NullLogger.Instance)
            {
                [Tool] = RunTool.CreateFromStaticMethod(reflector, NullLogger.Instance, Tool, typeof(ScaleTool).GetMethod(nameof(ScaleTool.Scale))!),
            };
            return new McpToolManager(NullLogger<McpToolManager>.Instance, reflector, tools) { ToolBinding = binding };
        }

        static IReadOnlyDictionary<string, JsonElement> Args(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        [Fact]
        public async Task BoundToolsListTheBindingsSignature()
        {
            var listed = (await Manager(new Binding()).RunListTool(new RequestListTool())).Value!.Single();
            listed.Description!.ShouldStartWith("bound: ");
            listed.InputSchema.GetProperty("properties").GetProperty("size").GetProperty("type").GetString().ShouldBe("string");
            listed.OutputSchema.ShouldBeNull();
        }

        [Fact]
        public async Task BoundArgumentsAndResultsSkipReflectorNet()
        {
            var r = (await Manager(new Binding()).RunCallTool(new RequestCallTool("r1", Tool, Args("{\"size\": \"3x4\", \"factor\": 10}")))).Value!;
            r.Status.ShouldBe(ResponseStatus.Success);
            r.Content.Single().Text.ShouldBe("30x40");   // factor (int) still converted by ReflectorNet: the binding declined it
        }

        [Fact]
        public async Task ABindingRejectionIsABadRequest()
        {
            var r = (await Manager(new Binding()).RunCallTool(new RequestCallTool("r1", Tool, Args("{\"size\": \"wide\"}")))).Value!;
            r.Status.ShouldBe(ResponseStatus.Error);
            r.Content.Single().Text!.ShouldContain("size must be WxH");
        }

        [Fact]
        public async Task WithoutABindingReflectorNetIsUsed()
        {
            var manager = Manager();
            var listed = (await manager.RunListTool(new RequestListTool())).Value!.Single();
            listed.InputSchema.GetRawText().ShouldContain("$defs");
            var r = (await manager.RunCallTool(new RequestCallTool("r1", Tool, Args("{\"size\": {\"Width\": 1, \"Height\": 2}}")))).Value!;
            r.Status.ShouldBe(ResponseStatus.Success);
            r.StructuredContent!.ToJsonString().ShouldContain("\"Width\":2");
        }

        [Fact]
        public async Task BindingsArePerManager()
        {
            // Two plugins in one process: one bound, one on ReflectorNet — no global state between them.
            var bound = (await Manager(new Binding()).RunListTool(new RequestListTool())).Value!.Single();
            var plain = (await Manager().RunListTool(new RequestListTool())).Value!.Single();
            bound.Description!.ShouldStartWith("bound: ");
            plain.Description!.ShouldNotStartWith("bound: ");
        }

        [Fact]
        public void SwitchingTheBindingAppliesToEveryToolAndTheTokenCount()
        {
            var manager = Manager();
            var tool = (RunTool)manager.GetAllTools().Single();
            var updates = 0;
            using var _ = manager.OnToolsUpdated.Subscribe(__ => updates++);
            var plainTokens = tool.TokenCount;

            manager.ToolBinding = new Binding();
            tool.Binding.ShouldBeSameAs(manager.ToolBinding);
            ((IRunTool)tool).Description!.ShouldStartWith("bound: ");
            tool.TokenCount.ShouldNotBe(plainTokens);   // counted from what the agent receives
            updates.ShouldBe(1);

            manager.ToolBinding = null;
            tool.Binding.ShouldBeNull();
            tool.TokenCount.ShouldBe(plainTokens);
        }

        sealed class ThrowingBinding : IToolBinding
        {
            public ToolSignature? Describe(string toolName, MethodInfo method, string? description) => throw new InvalidOperationException("boom");
            public bool TryConvert(MethodInfo method, ParameterInfo parameter, JsonElement value, out object? converted) { converted = null; return false; }
            public ResponseCallTool? Result(MethodInfo method, object? value) => null;
        }

        [Fact]
        public async Task AThrowingDescribeKeepsReflectorNetsSignatureAndTheListing()
        {
            var response = await Manager(new ThrowingBinding()).RunListTool(new RequestListTool());
            response.Status.ShouldBe(ResponseStatus.Success);
            response.Value!.Single().InputSchema.GetRawText().ShouldContain("$defs");
        }

        [Fact]
        public void ToolsAddedLaterGetTheBinding()
        {
            var manager = Manager(new Binding());
            var reflector = new Reflector();
            var added = RunTool.CreateFromStaticMethod(reflector, NullLogger.Instance, "later", typeof(ScaleTool).GetMethod(nameof(ScaleTool.Scale))!);
            manager.AddTool("later", added).ShouldBeTrue();
            added.Binding.ShouldBeSameAs(manager.ToolBinding);
        }
    }
}
