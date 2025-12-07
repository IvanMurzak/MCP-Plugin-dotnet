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
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public partial class RunTool
    {
        protected override JsonNode? CreateInputSchema(Reflector reflector, MethodInfo methodInfo)
        {
            var schema = base.CreateInputSchema(reflector, methodInfo);
            if (schema == null) return null;

            var parameters = methodInfo.GetParameters();
            foreach (var param in parameters)
            {
                if (param?.GetCustomAttribute<RequestIDAttribute>() != null)
                {
                    // Remove from properties
                    var properties = schema[JsonSchema.Properties]?.AsObject();
                    if (properties != null && param.Name != null)
                        properties.Remove(param.Name);

                    // Remove from required
                    var required = schema[JsonSchema.Required]?.AsArray();
                    if (required != null)
                    {
                        var toRemove = required.FirstOrDefault(x => x?.GetValue<string>() == param.Name);
                        if (toRemove != null)
                            required.Remove(toRemove);
                    }
                }
            }

            return schema;
        }
    }
}
