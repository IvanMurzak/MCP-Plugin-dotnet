/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// Finds lone UTF-16 surrogates (an unpaired <c>\uD800</c>-<c>\uDFFF</c> escape) in JSON. Such a string
    /// is valid JSON syntax but not valid text: System.Text.Json refuses to decode it, which used to surface
    /// as a bare JSON-RPC <c>-32603 "An error occurred."</c>.
    /// </summary>
    public static class Utf16Validation
    {
        /// <summary>The error text for a lone surrogate found at <paramref name="path"/>.</summary>
        public static string Describe(string path)
            => $"[invalid_utf16] Invalid params: '{path}' contains a lone UTF-16 surrogate (an unpaired \\uD800-\\uDFFF escape), " +
               "which is not valid text. Send a complete surrogate pair or the character itself.";

        /// <summary>The path of the first string (or property name) holding a lone surrogate, or null.</summary>
        public static string? Find(JsonElement value, string path)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    try
                    {
                        return HasLoneSurrogate(value.GetString()) ? path : null;
                    }
                    catch (InvalidOperationException)
                    {
                        return path; // System.Text.Json refuses to decode it
                    }
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                    {
                        string name;
                        try
                        {
                            name = property.Name;
                        }
                        catch (InvalidOperationException)
                        {
                            return path;
                        }
                        var childPath = Join(path, name);
                        if (HasLoneSurrogate(name))
                            return childPath;
                        var found = Find(property.Value, childPath);
                        if (found != null)
                            return found;
                    }
                    return null;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        var found = Find(item, $"{path}[{index++}]");
                        if (found != null)
                            return found;
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>The same as <see cref="Find(JsonElement, string)"/> for a <see cref="JsonNode"/> tree.</summary>
        public static string? Find(JsonNode? node, string path)
        {
            try
            {
                switch (node)
                {
                    case null:
                        return null;
                    case JsonObject obj:
                        foreach (var property in obj)
                        {
                            var childPath = Join(path, property.Key);
                            if (HasLoneSurrogate(property.Key))
                                return childPath;
                            var found = Find(property.Value, childPath);
                            if (found != null)
                                return found;
                        }
                        return null;
                    case JsonArray array:
                        for (var i = 0; i < array.Count; i++)
                        {
                            var found = Find(array[i], $"{path}[{i}]");
                            if (found != null)
                                return found;
                        }
                        return null;
                    case JsonValue value:
                        if (value.TryGetValue<JsonElement>(out var element))
                            return Find(element, path);
                        return value.TryGetValue<string>(out var text) && HasLoneSurrogate(text) ? path : null;
                    default:
                        return null;
                }
            }
            catch (InvalidOperationException)
            {
                return path; // a property name or value that cannot be decoded
            }
        }

        public static bool HasLoneSurrogate(string? text)
        {
            if (text == null)
                return false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                        return true;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }
            return false;
        }

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : $"{path}.{name}";
    }
}
