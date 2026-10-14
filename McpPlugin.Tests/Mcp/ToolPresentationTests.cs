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
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ToolPresentationTests
    {
        const string Tool = "greet";

        [Fact]
        public async Task List_GoesThroughThePresentation()
        {
            using var _ = Use(new Presentation());

            var listed = (await Manager().RunListTool(new RequestListTool())).Value!.Single();

            listed.Description.ShouldBe("presented");
            listed.InputSchema.GetProperty("type").GetString().ShouldBe("object");
        }

        [Fact]
        public async Task Call_ConvertsTheArgumentsAndTheResult()
        {
            using var _ = Use(new Presentation());

            var response = await Manager().RunCallTool(new RequestCallTool("r1", Tool, Args("{\"who\": \"ann\"}")));

            response.Value!.Status.ShouldBe(ResponseStatus.Success);
            response.Value.Content.Single().Text.ShouldBe("[presented] Hello ANN");
        }

        [Fact]
        public async Task Call_RejectedArgumentsDoNotRunTheTool()
        {
            using var _ = Use(new Presentation());

            var response = await Manager().RunCallTool(new RequestCallTool("r1", Tool, Args("{\"bad\": 1}")));

            response.Value!.Status.ShouldBe(ResponseStatus.Error);
            response.Value.Content.Single().Text!.ShouldContain("rejected");
            GreetTool.Calls.ShouldBe(0);
        }

        [Fact]
        public async Task WithoutAPresentation_TheToolIsUnchanged()
        {
            ToolPresentation.Current = null;

            var response = await Manager().RunCallTool(new RequestCallTool("r1", Tool, Args("{\"name\": \"bob\"}")));

            response.Value!.Content.Single().Text!.ShouldContain("Hello bob");
        }

        [Theory]
        [InlineData(ToolTextEscaping.Minimal, "a+b<c \"q\" \\ \n é", "\"a+b<c \\\"q\\\" \\\\ \\n é\"")]
        [InlineData(ToolTextEscaping.Ascii, "a+b é", "\"a+b \\u00e9\"")]
        [InlineData(ToolTextEscaping.Json, "a+b", "\"a\\u002Bb\"")]
        public void Quote_EscapesPerMode(ToolTextEscaping escaping, string text, string expected)
            => ToolPresentation.Quote(text, escaping).ShouldBe(expected);

        [Fact]
        public void Quote_EscapesLoneSurrogatesButKeepsPairs()
        {
            ToolPresentation.Quote("x\ud800y", ToolTextEscaping.Minimal).ShouldBe("\"x\\ud800y\"");
            ToolPresentation.Quote("😀", ToolTextEscaping.Minimal).ShouldBe("\"😀\"");
        }

        static McpToolManager Manager()
        {
            GreetTool.Calls = 0;
            var reflector = new Reflector();
            var tools = new ToolRunnerCollection(reflector, NullLogger.Instance)
            {
                [Tool] = RunTool.CreateFromStaticMethod(reflector, NullLogger.Instance, Tool, typeof(GreetTool).GetMethod(nameof(GreetTool.Greet))!),
            };
            return new McpToolManager(NullLogger<McpToolManager>.Instance, reflector, tools);
        }

        static IReadOnlyDictionary<string, JsonElement> Args(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        static IDisposable Use(IToolPresentation presentation)
        {
            ToolPresentation.Current = presentation;
            return new Reset();
        }

        sealed class Reset : IDisposable
        {
            public void Dispose() => ToolPresentation.Current = null;
        }

        static class GreetTool
        {
            public static int Calls;

            public static string Greet(string name)
            {
                Calls++;
                return "Hello " + name;
            }
        }

        // "who" is the agent-facing name of the tool's "name" argument, upper-cased on the way in.
        sealed class Presentation : IToolPresentation
        {
            public void List(IRunTool tool, ResponseListTool listed) => listed.Description = "presented";

            public string? Arguments(IRunTool tool, IReadOnlyDictionary<string, JsonElement> arguments, out IReadOnlyDictionary<string, JsonElement> converted)
            {
                converted = arguments;
                if (arguments.ContainsKey("bad")) return "rejected: bad argument";
                if (arguments.TryGetValue("who", out var who))
                    converted = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(who.GetString()!.ToUpperInvariant()) };
                return null;
            }

            public ResponseCallTool Result(IRunTool tool, ResponseCallTool result)
            {
                var text = string.Join("", result.Content.Select(c => c.Text));
                return ResponseCallTool.Success("[presented] " + JsonDocument.Parse(text).RootElement.GetProperty("result").GetString());
            }
        }
    }
}
