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
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public partial class RunTool
    {
        private int? _cachedTokenCount;

        /// <summary>
        /// Gets the semantic token count for this tool based on its JSON schema (including description).
        /// The token count is calculated from the JSON representation of the tool's input schema and description.
        /// This value is cached after the first calculation.
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
        /// Calculates the semantic token count for this tool.
        /// The calculation is based on the JSON Schema including name, title, description, input schema, and output schema.
        /// Uses a simple approximation: characters / 4 for semantic tokens (common for many LLM tokenizers).
        /// </summary>
        private int CalculateTokenCount()
        {
            try
            {
                // Build a JSON representation of the tool's schema using JsonObject
                var jsonObject = new JsonObject();

                // Add basic tool information
                if (!string.IsNullOrEmpty(Name))
                    jsonObject["name"] = Name;

                if (!string.IsNullOrEmpty(Title))
                    jsonObject["title"] = Title;

                if (!string.IsNullOrEmpty(Description))
                    jsonObject["description"] = Description;

                // Add schemas directly as JSON nodes
                if (InputSchema != null)
                    jsonObject["inputSchema"] = InputSchema;

                if (OutputSchema != null)
                    jsonObject["outputSchema"] = OutputSchema;

                // Serialize to JSON string (ensures proper escaping)
                var jsonString = jsonObject.ToJsonString();

                // Calculate tokens: using a common approximation of 1 token per 4 characters
                // This is a reasonable estimate for English text and JSON structures
                var tokenCount = (int)Math.Ceiling(jsonString.Length / 4.0);
                return tokenCount;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to calculate token count for tool '{0}'. Returning 0.", Name);
                return 0;
            }
        }
    }
}
