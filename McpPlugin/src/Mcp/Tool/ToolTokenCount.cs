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
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Shared helper for the semantic token-count approximation used by tools.
    /// Extracted from <see cref="RunTool.CalculateTokenCount"/> so that both the reflection-based
    /// <see cref="RunTool"/> and the runtime/proxy-based <see cref="ProxyTool"/> compute token counts
    /// from the identical chars/4 formula over the same JSON shape.
    ///
    /// <para>
    /// <b>Note:</b> This uses a simplified approximation of 1 token per 4 characters, which is a common
    /// heuristic but may not accurately reflect actual LLM tokenization. Different models use different
    /// tokenizers (e.g., GPT uses tiktoken, Claude uses a different tokenization scheme).
    /// This approximation provides a reasonable estimate for planning and capacity management but should
    /// not be relied upon for exact token accounting.
    /// </para>
    /// </summary>
    internal static class ToolTokenCount
    {
        /// <summary>
        /// Calculates the semantic token count from a tool's name, title, description, input schema, and
        /// output schema. The calculation builds a JSON object containing the non-empty fields and the
        /// (non-null) schema nodes, serializes it, and returns <c>ceil(length / 4)</c>.
        /// </summary>
        /// <remarks>
        /// The schema nodes are inserted via a detached deep copy (round-trip through
        /// <see cref="JsonNode.ToJsonString(System.Text.Json.JsonSerializerOptions)"/> +
        /// <see cref="JsonNode.Parse(string, System.Text.Json.Nodes.JsonNodeOptions?, System.Text.Json.JsonDocumentOptions)"/>)
        /// so that the caller's live <see cref="JsonNode"/> is never re-parented (a <see cref="JsonNode"/>
        /// may only have a single parent). The serialized JSON — and therefore the resulting count — is
        /// identical to assigning the node directly, so this is behavior-preserving for the numeric result.
        /// </remarks>
        public static int Calculate(string? name, string? title, string? description, JsonNode? inputSchema, JsonNode? outputSchema)
        {
            // Build a JSON representation of the tool's schema using JsonObject
            var jsonObject = new JsonObject();

            // Add basic tool information
            if (!string.IsNullOrEmpty(name))
                jsonObject["name"] = name;

            if (!string.IsNullOrEmpty(title))
                jsonObject["title"] = title;

            if (!string.IsNullOrEmpty(description))
                jsonObject["description"] = description;

            // Add schemas as detached copies so the caller's live nodes are not re-parented.
            if (inputSchema != null)
                jsonObject["inputSchema"] = JsonNode.Parse(inputSchema.ToJsonString());

            if (outputSchema != null)
                jsonObject["outputSchema"] = JsonNode.Parse(outputSchema.ToJsonString());

            // Serialize to JSON string (ensures proper escaping)
            var jsonString = jsonObject.ToJsonString();

            // Calculate tokens: using a common approximation of 1 token per 4 characters
            // This is a reasonable estimate for English text and JSON structures
            return (int)Math.Ceiling(jsonString.Length / 4.0);
        }
    }
}
