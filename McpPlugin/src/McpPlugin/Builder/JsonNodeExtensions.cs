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
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public static class JsonNodeExtensions
    {
        public static List<ResponsePromptArgument>? ToResponsePromptArguments(this JsonNode? node)
        {
            if (node == null)
                return null;

            if (node is not JsonObject obj)
                return null;

            if (!obj.TryGetPropertyValue(JsonSchema.Properties, out var propertiesNode))
                return null;

            if (propertiesNode is not JsonObject propertiesObj)
                return null;

            return propertiesObj
                .Select(input =>
                {
                    if (input.Value is not JsonObject inputObj)
                        return null;

                    inputObj.TryGetPropertyValue(JsonSchema.Description, out var descriptionNode);
                    inputObj.TryGetPropertyValue(JsonSchema.Required, out var requiredNode);

                    var requiredSet = requiredNode is JsonArray
                        ? requiredNode.AsArray()
                            .Select(v => v?.GetValue<string>())
                            .Where(v => !string.IsNullOrEmpty(v))
                            .Select(v => v!)
                            .ToHashSet()
                        : null;

                    return new ResponsePromptArgument()
                    {
                        Name = input.Key,
                        Description = descriptionNode?.GetValue<string>(),
                        Required = requiredSet?.Contains(input.Key) ?? false,
                    };
                })
                .Where(arg => arg != null)
                .Select(arg => arg!)
                .ToList();
        }
    }
}
