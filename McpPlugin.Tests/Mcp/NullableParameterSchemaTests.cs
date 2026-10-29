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
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    public static class NullableParameterTools
    {
        [AiTool("nullable-params")]
        public static string Run(string query, string? filter, int? limit) => $"{query}|{filter ?? "-"}|{limit?.ToString() ?? "-"}";
    }

    /// <summary>
    /// Wave-6 finding: an optional <c>string? x</c> parameter without a default was listed as required (ReflectorNet), so
    /// the server's argument validation rejected calls that leave it out. With ReflectorNet 5.5.1 it is optional.
    /// </summary>
    [Collection("McpPlugin")]
    public class NullableParameterSchemaTests
    {
        static IMcpPlugin Build() => new McpPluginBuilder(new Version())
            .WithTools(typeof(NullableParameterTools))
            .Build(new Reflector());

        [Fact]
        public async Task NullableParameters_AreNotRequired_InTheListedInputSchema()
        {
            using var plugin = Build();
            var listed = (await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool())).Value!.Single(t => t.Name == "nullable-params");
            var required = listed.InputSchema.TryGetProperty("required", out var array)
                ? array.EnumerateArray().Select(e => e.GetString()).ToArray()
                : new string?[0];
            required.ShouldBe(new[] { "query" });
        }

        [Fact]
        public async Task NullableParameters_LeftOut_AreNull()
        {
            using var plugin = Build();
            var response = await plugin.McpManager.ToolManager!.RunCallTool(new RequestCallTool("r1", "nullable-params",
                new Dictionary<string, JsonElement> { ["query"] = JsonDocument.Parse("\"q\"").RootElement }));
            response.Status.ShouldBe(ResponseStatus.Success, response.Message);
            JsonNode.Parse(response.Value!.Content!.First().Text!)!["result"]!.GetValue<string>().ShouldBe("q|-|-");
        }
    }
}
