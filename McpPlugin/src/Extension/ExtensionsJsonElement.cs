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

namespace com.IvanMurzak.McpPlugin
{
    public static class ExtensionsJsonElement
    {
        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new int value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, int newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetInt32(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new uint value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, uint newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetUInt32(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new long value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, long newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetInt64(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new ulong value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, ulong newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetUInt64(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new float value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, float newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetSingle(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new double value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, double newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetDouble(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new decimal value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, decimal newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.TryGetDecimal(out var existing) && existing == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteNumber(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new string value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, string newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && prop.ValueKind == JsonValueKind.String && prop.GetString() == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteString(name, newValue));
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new boolean value.
        /// </summary>
        public static JsonElement SetProperty(this ref JsonElement? originalElement, string propertyName, bool newValue)
        {
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var prop)
                && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
                && prop.GetBoolean() == newValue)
                return originalElement.Value;

            return SetPropertyCore(ref originalElement, propertyName,
                (w, name) => w.WriteBoolean(name, newValue));
        }

        /// <summary>
        /// Shared implementation that copies all existing properties (except the target),
        /// writes the new property via <paramref name="writeValue"/>, and parses the result back.
        /// </summary>
        private static JsonElement SetPropertyCore(
            ref JsonElement? originalElement,
            string propertyName,
            Action<Utf8JsonWriter, string> writeValue)
        {
            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            if (originalElement == null)
            {
                writeValue(writer, propertyName);
            }
            else
            {
                foreach (var property in originalElement.Value.EnumerateObject())
                {
                    if (property.Name != propertyName)
                    {
                        property.WriteTo(writer);
                    }
                }
                writeValue(writer, propertyName);
            }

            writer.WriteEndObject();
            writer.Flush();

            var jsonBytes = new ReadOnlyMemory<byte>(stream.GetBuffer(), 0, (int)stream.Length);
            using var doc = JsonDocument.Parse(jsonBytes);
            originalElement = doc.RootElement.Clone();
            return originalElement.Value;
        }
    }
}
