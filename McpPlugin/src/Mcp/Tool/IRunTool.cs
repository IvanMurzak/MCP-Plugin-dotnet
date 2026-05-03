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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    public interface IRunTool : IEnabled
    {
        string Name { get; }
        string? Title { get; }
        string? Description { get; }

        /// <summary>
        /// Optional concise description used for the SKILL.md YAML <c>description:</c> field.
        /// When <see langword="null"/>, <see cref="Skills.SkillFileGenerator"/> falls back to <see cref="Description"/>
        /// (truncated to fit the YAML cap).
        /// Sourced from <see cref="McpPluginSkillDescriptionAttribute"/> on the underlying method by default.
        /// </summary>
        string? SkillDescription { get; }

        /// <summary>
        /// Optional long-form markdown injected into the SKILL.md body between the description paragraph
        /// and the <c>## How to Call</c> section. Lets tools ship rich content (code samples, notes) that
        /// would otherwise overflow the YAML <c>description:</c> cap.
        /// Sourced from <see cref="McpPluginSkillBodyAttribute"/> on the underlying method by default.
        /// </summary>
        string? SkillBody { get; }

        JsonNode? InputSchema { get; }
        JsonNode? OutputSchema { get; }

        /// <summary>
        /// The type of tool. Standard tools are exposed to MCP clients;
        /// System tools are only available via the HTTP API.
        /// Defaults to <see cref="McpToolType.Standard"/>.
        /// </summary>
        McpToolType ToolType => McpToolType.Standard;

        /// <summary>
        /// If true, the tool only reads or queries data and does not modify system state.
        /// </summary>
        bool? ReadOnlyHint { get; }

        /// <summary>
        /// If true, the tool may perform destructive updates (e.g., deleting data, overwriting files).
        /// </summary>
        bool? DestructiveHint { get; }

        /// <summary>
        /// If true, calling the tool multiple times with the same arguments will have no additional effect
        /// on its environment beyond the first call.
        /// </summary>
        bool? IdempotentHint { get; }

        /// <summary>
        /// If true, the tool may interact with an "open world" of external entities
        /// (e.g., the web, external APIs, or real-world systems).
        /// </summary>
        bool? OpenWorldHint { get; }

        /// <summary>
        /// Gets the semantic token count for this tool based on its JSON schema (including description).
        /// </summary>
        int TokenCount { get; }

        /// <summary>
        /// Executes the target method with named parameters.
        /// Missing parameters will be filled with their default values or the type's default value if no default is defined.
        /// </summary>
        /// <param name="namedParameters">A dictionary mapping parameter names to their values.</param>
        /// <returns>The result of the method execution, or null if the method is void.</returns>
        Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default);
    }
}
