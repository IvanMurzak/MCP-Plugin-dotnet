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
        /// The calculation is based on the JSON Schema including name, title, description, and input schema.
        /// Uses a simple approximation: characters / 4 for semantic tokens (common for many LLM tokenizers).
        /// </summary>
        private int CalculateTokenCount()
        {
            try
            {
                // Create a JSON object representing the tool's schema
                var toolSchema = new JsonObject();
                
                // Add basic tool information
                if (!string.IsNullOrEmpty(Name))
                    toolSchema["name"] = Name;
                    
                if (!string.IsNullOrEmpty(Title))
                    toolSchema["title"] = Title;
                    
                if (!string.IsNullOrEmpty(Description))
                    toolSchema["description"] = Description;
                
                // Add input schema
                if (InputSchema != null)
                    toolSchema["inputSchema"] = InputSchema.DeepClone();
                
                // Add output schema if present
                if (OutputSchema != null)
                    toolSchema["outputSchema"] = OutputSchema.DeepClone();
                
                // Serialize to JSON string
                var jsonString = toolSchema.ToJsonString(new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                });
                
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
