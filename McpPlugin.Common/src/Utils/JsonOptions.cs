/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Text.Json;
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.Common.Utils
{
    public static class JsonOptions
    {
        const string NullJson = "null";
        public static readonly JsonSerializerOptions Pretty = new()
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = false,
            // ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        public static string ToPrettyJson<T>(this T obj)
        {
            if (obj == null)
                return NullJson;

            return JsonSerializer.Serialize(obj, Pretty);
        }
    }
}
