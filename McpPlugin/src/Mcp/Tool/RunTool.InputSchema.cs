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
            if (schema == null) return null;

            ArgumentUtils.RemoveRequestIDParameters(schema, methodInfo);

            return schema;
        }
    }
}
