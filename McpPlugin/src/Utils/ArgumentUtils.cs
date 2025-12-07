/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin.Utils
{
    public static class ArgumentUtils
    {
        public static void RemoveRequestIDParameters(JsonNode schema, MethodInfo methodInfo)
        {
            // Pre-fetch schema nodes to avoid repeated lookups
            var properties = schema[JsonSchema.Properties]?.AsObject();
            var required = schema[JsonSchema.Required]?.AsArray();

            // If neither exists, there's nothing to modify
            if (properties == null && required == null)
                return;

            foreach (var param in methodInfo.GetParameters())
            {
                // Use IsDefined for faster attribute checking
                if (param.IsDefined(typeof(RequestIDAttribute), false))
                {
                    var name = param.Name;
                    if (string.IsNullOrEmpty(name)) continue;

                    // Remove from properties
                    properties?.Remove(name);

                    // Remove from required
                    if (required != null)
                    {
                        var nodeToRemove = required.FirstOrDefault(x => x?.GetValue<string>() == name);
                        if (nodeToRemove != null)
                            required.Remove(nodeToRemove);
                    }
                }
            }
        }
    }
}
