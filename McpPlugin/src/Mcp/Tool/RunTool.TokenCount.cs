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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public partial class RunTool
    {
        private int? _cachedTokenCount;
        private int? _cachedSkillMetadataTokenCount;

        /// <summary>
        /// Gets the semantic token count for this tool based on its JSON schema (including description and
        /// the published skill metadata). The token count is calculated from the JSON representation of the
        /// tool's name, title, description, <see cref="SkillDescription"/> / <see cref="SkillBody"/>, input
        /// schema and output schema. This value is cached after the first calculation.
        ///
        /// <para>
        /// <b>Note:</b> This uses a simplified approximation of 1 token per 4 characters, which is a common
        /// heuristic but may not accurately reflect actual LLM tokenization. Different models use different
        /// tokenizers (e.g., GPT uses tiktoken, Claude uses a different tokenization scheme).
        /// This approximation provides a reasonable estimate for planning and capacity management but should
        /// not be relied upon for exact token accounting.
        /// </para>
        /// </summary>
        public int TokenCount
        {
            get
            {
                if (_cachedTokenCount.HasValue)
                    return _cachedTokenCount.Value;

                _cachedTokenCount = CalculateTokenCount();
                return _cachedTokenCount.Value;
            }
        }

        /// <summary>
        /// The portion of <see cref="TokenCount"/> contributed by this tool's published skill metadata
        /// (<see cref="SkillDescription"/> / <see cref="SkillBody"/>), including its JSON key overhead.
        /// Cached on first read, like <see cref="TokenCount"/>, and for the same reason: both inputs are read
        /// from attributes on the underlying <c>Method</c>, which this type never rebinds, so no cache
        /// invalidation is required.
        /// </summary>
        public int SkillMetadataTokenCount
        {
            get
            {
                if (_cachedSkillMetadataTokenCount.HasValue)
                    return _cachedSkillMetadataTokenCount.Value;

                _cachedSkillMetadataTokenCount = CalculateSkillMetadataTokenCount();
                return _cachedSkillMetadataTokenCount.Value;
            }
        }

        /// <summary>
        /// Calculates the semantic token count for this tool.
        /// The calculation is based on the JSON Schema including name, title, description, published skill
        /// metadata, input schema, and output schema.
        /// Uses a simple approximation: characters / 4 for semantic tokens (common for many LLM tokenizers).
        /// Delegates to the shared <see cref="ToolTokenCount.Calculate"/> helper so that the formula is
        /// identical to <see cref="ProxyTool"/>.
        /// </summary>
        private int CalculateTokenCount()
        {
            try
            {
                return ToolTokenCount.Calculate(Name, Title, Description, SkillDescription, SkillBody, InputSchema, OutputSchema);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to calculate token count for tool '{0}'. Returning 0.", Name);
                return 0;
            }
        }

        /// <summary>
        /// Calculates the skill-metadata share of this tool's token count, with the same
        /// return-0-on-failure resilience <see cref="CalculateTokenCount"/> has: a breakdown must never
        /// be able to poison <see cref="IToolManager.EnabledToolsSkillMetadataTokenCount"/>.
        /// </summary>
        private int CalculateSkillMetadataTokenCount()
        {
            try
            {
                return ToolTokenCount.CalculateSkillMetadata(Name, Title, Description, SkillDescription, SkillBody, InputSchema, OutputSchema);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to calculate skill-metadata token count for tool '{0}'. Returning 0.", Name);
                return 0;
            }
        }
    }
}
