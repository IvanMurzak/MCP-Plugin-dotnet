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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// Checks <c>tools/call</c> arguments against the tool's input schema before the call reaches the
    /// engine: every plainly typed <c>required</c> argument is present (and not null unless its schema allows
    /// null), and every argument whose schema
    /// names a JSON <c>type</c> has that type. Anything the schema does not state plainly
    /// (<c>$ref</c>, untyped, unknown keywords, extra arguments) is left to the plugin.
    /// <para>
    /// Lenient where the plugin's deserializer (ReflectorNet) is: a numeric string passes for
    /// <c>integer</c>/<c>number</c>, a number or <c>"true"</c>/<c>"false"</c> for <c>boolean</c>, an object or
    /// array for <c>string</c> (passed as its JSON text) and an integer for a string <c>enum</c>. A plain number
    /// or boolean never passes for <c>string</c>.
    /// </para>
    /// </summary>
    public static class ToolArgumentValidator
    {
        static readonly string[] AlternativeKeywords = { "anyOf", "oneOf" };

        /// <summary>
        /// Returns null when the arguments fit <paramref name="inputSchema"/>, otherwise an error naming
        /// every offending argument. With <paramref name="checkMissingRequired"/> false (a plugin built on
        /// McpPlugin before 8.14.0, see <see cref="PluginArgumentPolicy"/>) an omitted required argument is left
        /// to the plugin, as before 8.14.0; every type check still applies.
        /// </summary>
        public static string? Validate(string toolName, JsonElement inputSchema, IReadOnlyDictionary<string, JsonElement>? arguments,
            bool checkMissingRequired = true)
        {
            if (inputSchema.ValueKind != JsonValueKind.Object)
                return null;

            arguments ??= new Dictionary<string, JsonElement>();
            var properties = inputSchema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
                ? props
                : (JsonElement?)null;
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (inputSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in req.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } name)
                        required.Add(name);
            }

            var problems = new List<string>();
            foreach (var name in required)
            {
                // A required argument whose schema is not stated plainly ($ref, untyped, not listed) is left to the
                // plugin: ReflectorNet before 5.5.1 listed a nullable parameter without a default (an int? is a $ref)
                // as required, and the plugin accepts it missing.
                if (!properties.HasValue || !TryGetProperty(properties.Value, name, out var propSchema) || !(AllowedTypes(propSchema) is { } allowed))
                    continue;
                var present = TryGetArgument(arguments, name, out var value) && value.ValueKind != JsonValueKind.Undefined;
                if (!present)
                {
                    if (checkMissingRequired)
                        problems.Add($"missing required argument '{name}' ({DescribeTypes(allowed)})");
                }
                // An explicit null passes for a required argument whose schema allows null. Otherwise it is there with the
                // wrong type, and says so (the type check below skips nulls); other types are checked below.
                else if (value.ValueKind == JsonValueKind.Null && !allowed.Contains("null") && !IsNullable(propSchema))
                    problems.Add(WrongType(name, allowed, value));
            }

            if (properties.HasValue)
            {
                foreach (var argument in arguments)
                {
                    if (!TryGetProperty(properties.Value, argument.Key, out var propSchema))
                        continue;
                    var value = argument.Value;
                    if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        continue; // a null required argument is reported above; a null optional one means "not given"
                    var allowed = AllowedTypes(propSchema);
                    if (allowed == null || allowed.Any(type => Matches(value, type, propSchema)))
                        continue;
                    problems.Add(WrongType(argument.Key, allowed, value));
                }
            }

            if (problems.Count == 0)
                return null;
            return $"{ToolCallErrors.InvalidParams} Invalid params for tool '{toolName}': {string.Join("; ", problems)}.";
        }

        /// <summary>An argument that is present with a JSON type its schema does not allow.</summary>
        static string WrongType(string name, IReadOnlyList<string> allowed, JsonElement value)
            => $"argument '{name}' must be {DescribeTypes(allowed)}, got {Describe(value)}";

        /// <summary>OpenAPI-style <c>"nullable": true</c>.</summary>
        static bool IsNullable(JsonElement propertySchema)
            => propertySchema.ValueKind == JsonValueKind.Object
                && propertySchema.TryGetProperty("nullable", out var nullable)
                && nullable.ValueKind == JsonValueKind.True;

        /// <summary>The JSON types a property schema allows, or null when it does not say plainly.</summary>
        static IReadOnlyList<string>? AllowedTypes(JsonElement propertySchema)
        {
            if (propertySchema.ValueKind != JsonValueKind.Object)
                return null;
            if (propertySchema.TryGetProperty("type", out var type))
                return TypeNames(type);

            // "anyOf"/"oneOf" of plainly-typed alternatives (e.g. a nullable value): the union of their types.
            foreach (var keyword in AlternativeKeywords)
            {
                if (!propertySchema.TryGetProperty(keyword, out var alternatives) || alternatives.ValueKind != JsonValueKind.Array)
                    continue;
                var union = new List<string>();
                foreach (var alternative in alternatives.EnumerateArray())
                {
                    if (alternative.ValueKind != JsonValueKind.Object || !alternative.TryGetProperty("type", out var altType))
                        return null;
                    var names = TypeNames(altType);
                    if (names == null)
                        return null;
                    union.AddRange(names);
                }
                return union.Count == 0 ? null : union.Distinct(StringComparer.Ordinal).ToList();
            }
            return null;
        }

        static IReadOnlyList<string>? TypeNames(JsonElement type)
        {
            if (type.ValueKind == JsonValueKind.String)
                return IsKnownType(type.GetString()) ? new[] { type.GetString()! } : null;
            if (type.ValueKind != JsonValueKind.Array)
                return null;
            var names = new List<string>();
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !IsKnownType(item.GetString()))
                    return null;
                names.Add(item.GetString()!);
            }
            return names.Count == 0 ? null : names;
        }

        static bool IsKnownType(string? type)
            => type is "string" or "integer" or "number" or "boolean" or "object" or "array" or "null";

        static bool Matches(JsonElement value, string type, JsonElement propertySchema) => type switch
        {
            // The plugin passes an object/array to a string parameter as its JSON text, and reads an enum from its number.
            "string" => value.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array
                || (value.ValueKind == JsonValueKind.Number && propertySchema.TryGetProperty("enum", out _) && IsInteger(value)),
            "integer" => IsInteger(value),
            "number" => value.ValueKind == JsonValueKind.Number
                || (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number
                || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out _)),
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        };

        static bool IsInteger(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number)
                return value.TryGetInt64(out _) || (value.TryGetDecimal(out var d) && decimal.Truncate(d) == d);
            return value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        static bool TryGetArgument(IReadOnlyDictionary<string, JsonElement> arguments, string name, out JsonElement value)
        {
            // The dictionary's own comparer first (the REST API's is case-insensitive), then a case-insensitive scan.
            if (arguments.TryGetValue(name, out value))
                return true;
            foreach (var pair in arguments)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        static bool TryGetProperty(JsonElement properties, string name, out JsonElement schema)
        {
            if (properties.TryGetProperty(name, out schema))
                return true;
            foreach (var property in properties.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    schema = property.Value;
                    return true;
                }
            }
            schema = default;
            return false;
        }

        static string? DescribeTypes(IReadOnlyList<string>? types)
        {
            if (types == null || types.Count == 0)
                return null;
            var named = types.Select(t => t switch
            {
                "integer" => "an integer",
                "object" => "an object",
                "array" => "an array",
                "null" => "null",
                _ => "a " + t,
            }).ToList();
            return named.Count == 1 ? named[0] : string.Join(" or ", named);
        }

        static string Describe(JsonElement value)
        {
            var kind = value.ValueKind switch
            {
                JsonValueKind.String => "a string",
                JsonValueKind.Number => "a number",
                JsonValueKind.True or JsonValueKind.False => "a boolean",
                JsonValueKind.Object => "an object",
                JsonValueKind.Array => "an array",
                _ => value.ValueKind.ToString().ToLowerInvariant(),
            };
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return $"{kind} ({value.GetRawText()})";
            return kind;
        }
    }
}
