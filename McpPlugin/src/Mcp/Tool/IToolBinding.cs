/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// EXPERIMENTAL. Replaces ReflectorNet as the data layer of reflected tools (<see cref="RunTool"/>): what the agent
    /// sees (description, input / output schema), how each argument becomes the parameter's .NET value, and how a returned
    /// value becomes the response. The tool methods are unchanged. Set per tool manager
    /// (<see cref="IToolManager.ToolBinding"/>, applied to every <see cref="RunTool"/> it holds); null = ReflectorNet.
    /// </summary>
    public interface IToolBinding
    {
        /// <summary>The agent-facing signature of <paramref name="method"/>; null keeps ReflectorNet's.</summary>
        ToolSignature? Describe(string toolName, MethodInfo method, string? description);

        /// <summary>
        /// Convert the argument the agent sent for <paramref name="parameter"/>. Return false to keep ReflectorNet's
        /// conversion; throw <see cref="System.ArgumentException"/> to reject the call with a message for the agent.
        /// </summary>
        bool TryConvert(MethodInfo method, ParameterInfo parameter, JsonElement value, out object? converted);

        /// <summary>The response for what the method returned (a value or a <see cref="ResponseCallTool"/>); null keeps the default.</summary>
        ResponseCallTool? Result(MethodInfo method, object? value);
    }

    /// <summary>What the agent sees of a tool.</summary>
    public sealed class ToolSignature
    {
        public ToolSignature(string? description, JsonNode inputSchema, JsonNode? outputSchema = null)
        {
            Description = description;
            InputSchema = inputSchema;
            OutputSchema = outputSchema;
        }

        public string? Description { get; }
        public JsonNode InputSchema { get; }
        public JsonNode? OutputSchema { get; }
    }
}
