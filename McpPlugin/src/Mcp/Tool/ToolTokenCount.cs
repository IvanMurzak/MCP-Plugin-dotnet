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
        /// JSON key the skill description is measured under. Deliberately the same spelling the wire uses
        /// for <c>tools/list</c> <c>_meta.skillDescription</c> (McpPlugin.Server's <c>ExtensionsListMeta</c>),
        /// so the instrument measures the payload that is actually shipped.
        /// <para>
        /// <b>This is a SECOND declaration of that spelling, not a reference to it</b> — McpPlugin.Server
        /// cannot be referenced from here, and the measured count depends on the key's LENGTH, so a rename on
        /// one side alone would silently shift every skill-bearing tool's share. Each side is pinned to the
        /// literal by its own test, so a one-sided rename reddens that side; keeping them in step is a
        /// convention, not a compile-time guarantee. Single-sourcing the pair in McpPlugin.Common (which both
        /// assemblies already reference) would make it one, at the cost of new public surface there.
        /// </para>
        /// </summary>
        public const string SkillDescriptionKey = "skillDescription";

        /// <summary>
        /// JSON key the skill body is measured under. Same wire-parity rule as
        /// <see cref="SkillDescriptionKey"/> (<c>tools/list</c> <c>_meta.skillBody</c>).
        /// </summary>
        public const string SkillBodyKey = "skillBody";

        /// <summary>
        /// Calculates the semantic token count from a tool's name, title, description, published skill
        /// metadata, input schema, and output schema. The calculation builds a JSON object containing the
        /// non-empty fields and the (non-null) schema nodes, serializes it, and returns <c>ceil(length / 4)</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The skill members are measured FLAT, alongside the other fields, whereas the wire nests them inside
        /// a <c>_meta</c> object. For an enabled tool that object exists only because of them, so the figure
        /// omits its own <c>,"_meta":{</c> … <c>}</c> wrapper — about ten characters, two to three tokens per
        /// skill-bearing tool. That is deliberate: the members are counted under the same flat accounting
        /// every other member gets, rather than in a unit of their own. Treat the result as a catalogue-weight
        /// estimate, not as byte-exact wire cost.
        /// </para>
        /// <para>
        /// The schema nodes are inserted via a detached deep copy (round-trip through
        /// <see cref="JsonNode.ToJsonString(System.Text.Json.JsonSerializerOptions)"/> +
        /// <see cref="JsonNode.Parse(string, System.Text.Json.Nodes.JsonNodeOptions?, System.Text.Json.JsonDocumentOptions)"/>)
        /// so that the caller's live <see cref="JsonNode"/> is never re-parented (a <see cref="JsonNode"/>
        /// may only have a single parent). The serialized JSON — and therefore the resulting count — is
        /// identical to assigning the node directly, so this is behavior-preserving for the numeric result.
        /// </para>
        /// </remarks>
        public static int Calculate(
            string? name,
            string? title,
            string? description,
            string? skillDescription,
            string? skillBody,
            JsonNode? inputSchema,
            JsonNode? outputSchema)
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

            // Published skill metadata. It reaches every MCP client on the tools/list entry as
            // _meta.skillDescription / _meta.skillBody, gated there on the SAME string.IsNullOrEmpty
            // test used here — so an absent value costs nothing and a present one is measured exactly
            // once, under the same accounting the members above get.
            if (!string.IsNullOrEmpty(skillDescription))
                jsonObject[SkillDescriptionKey] = skillDescription;

            if (!string.IsNullOrEmpty(skillBody))
                jsonObject[SkillBodyKey] = skillBody;

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

        /// <summary>
        /// The portion of <see cref="Calculate"/>'s result contributed by the published skill metadata,
        /// including the JSON key and separator overhead the two members bring with them. Defined as the
        /// difference between the full count and the count of the identical tool with no skill metadata,
        /// so <c>total - share</c> is exactly the count the entry carried before that metadata reached
        /// the wire, and the meaning of the total itself is unchanged for every existing caller.
        /// Returns <c>0</c> when the tool publishes no skill metadata.
        /// </summary>
        public static int CalculateSkillMetadata(
            string? name,
            string? title,
            string? description,
            string? skillDescription,
            string? skillBody,
            JsonNode? inputSchema,
            JsonNode? outputSchema)
        {
            if (string.IsNullOrEmpty(skillDescription) && string.IsNullOrEmpty(skillBody))
                return 0;

            return Calculate(name, title, description, skillDescription, skillBody, inputSchema, outputSchema)
                 - Calculate(name, title, description, null, null, inputSchema, outputSchema);
        }
    }
}
