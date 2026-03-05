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
using System.Reflection;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Utils;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public partial class RunTool
    {
        protected override JsonNode? CreateInputSchema(Reflector reflector, MethodInfo methodInfo)
        {
            var schema = base.CreateInputSchema(reflector, methodInfo);
            // Tools require an input schema (even if empty) to comply with MCP protocol.
            // In contrast, prompts can have null input schema. See RunPrompt.InputSchema for the difference.
            if (schema == null) return Common.Consts.MCP.EmptyInputSchemaNode;

            if (schema is not JsonObject schemaObject)
                throw new InvalidOperationException("Expected schema to be a JsonObject.");

            // Validate that schema type is "object"
            if (!schemaObject.TryGetPropertyValue(JsonSchema.Type, out var type) || type?.GetValue<string>() != JsonSchema.Object)
                throw new InvalidOperationException("Expected schema type to be 'object'.");

            // For single-property schemas (typically just type), add additionalProperties: false
            if (schemaObject.Count == 1 && schemaObject.TryGetPropertyValue(JsonSchema.Type, out _))
                schemaObject[JsonSchema.AdditionalProperties] = false;

            ArgumentUtils.RemoveRequestIDParameters(schema, methodInfo);

            return schema;
        }
    }
}
