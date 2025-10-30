/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text;
using System.Text.Json;

namespace com.IvanMurzak.McpPlugin
{
    public static class ExtensionsJsonElement
    {
        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new value.
        /// </summary>
        /// <param name="originalElement">The original JsonElement to update</param>
        /// <param name="propertyName">The name of the property to set/replace</param>
        /// <param name="newValue">The new value for the property</param>
        /// <returns>A new JsonElement with the updated property</returns>
        public static JsonElement SetProperty(
            this ref JsonElement? originalElement,
            string propertyName,
            int newValue)
        {
            // Check if need to set value
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var propertyElement))
            {
                if (propertyElement.TryGetInt32(out var existedValue))
                {
                    if (existedValue == newValue)
                        return originalElement.Value; // no need to set value
                }
            }

            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            if (originalElement == null)
            {
                // If originalElement is null, we just write the new property
                writer.WriteNumber(propertyName, newValue);
            }
            else
            {
                // Copy all existing properties except the one we're updating
                foreach (var property in originalElement.Value.EnumerateObject())
                {
                    if (property.Name != propertyName)
                    {
                        property.WriteTo(writer);
                    }
                }
                // Write the new property value
                writer.WriteNumber(propertyName, newValue);
            }

            writer.WriteEndObject();
            writer.Flush();

            // Parse and return the new JsonElement
            var correctedJson = Encoding.UTF8.GetString(stream.ToArray());
            originalElement = JsonDocument.Parse(correctedJson).RootElement;
            return originalElement.Value;
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new string value.
        /// </summary>
        /// <param name="originalElement">The original JsonElement to update</param>
        /// <param name="propertyName">The name of the property to set/replace</param>
        /// <param name="newValue">The new string value for the property</param>
        /// <returns>A new JsonElement with the updated property</returns>
        public static JsonElement SetProperty(
            this ref JsonElement? originalElement,
            string propertyName,
            string newValue)
        {
            // Check if need to set value
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var propertyElement))
            {
                if (propertyElement.ValueKind == JsonValueKind.String)
                {
                    var existedValue = propertyElement.GetString();
                    if (existedValue == newValue)
                        return originalElement.Value; // no need to set value
                }
            }

            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            if (originalElement == null)
            {
                // If originalElement is null, we just write the new property
                writer.WriteString(propertyName, newValue);
            }
            else
            {
                // Copy all existing properties except the one we're updating
                foreach (var property in originalElement.Value.EnumerateObject())
                {
                    if (property.Name != propertyName)
                    {
                        property.WriteTo(writer);
                    }
                }
                // Write the new property value
                writer.WriteString(propertyName, newValue);
            }

            writer.WriteEndObject();
            writer.Flush();

            // Parse and return the new JsonElement
            var correctedJson = Encoding.UTF8.GetString(stream.ToArray());
            originalElement = JsonDocument.Parse(correctedJson).RootElement;
            return originalElement.Value;
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new boolean value.
        /// </summary>
        /// <param name="originalElement">The original JsonElement to update</param>
        /// <param name="propertyName">The name of the property to set/replace</param>
        /// <param name="newValue">The new boolean value for the property</param>
        /// <returns>A new JsonElement with the updated property</returns>
        public static JsonElement SetProperty(
            this ref JsonElement? originalElement,
            string propertyName,
            bool newValue)
        {
            // Check if need to set value
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var propertyElement))
            {
                if (propertyElement.ValueKind == JsonValueKind.True || propertyElement.ValueKind == JsonValueKind.False)
                {
                    var existedValue = propertyElement.GetBoolean();
                    if (existedValue == newValue)
                        return originalElement.Value; // no need to set value
                }
            }

            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            if (originalElement == null)
            {
                // If originalElement is null, we just write the new property
                writer.WriteBoolean(propertyName, newValue);
            }
            else
            {
                // Copy all existing properties except the one we're updating
                foreach (var property in originalElement.Value.EnumerateObject())
                {
                    if (property.Name != propertyName)
                    {
                        property.WriteTo(writer);
                    }
                }
                // Write the new property value
                writer.WriteBoolean(propertyName, newValue);
            }

            writer.WriteEndObject();
            writer.Flush();

            // Parse and return the new JsonElement
            var correctedJson = Encoding.UTF8.GetString(stream.ToArray());
            originalElement = JsonDocument.Parse(correctedJson).RootElement;
            return originalElement.Value;
        }

        /// <summary>
        /// Updates a JsonElement by setting or replacing a specific property with a new float value.
        /// </summary>
        /// <param name="originalElement">The original JsonElement to update</param>
        /// <param name="propertyName">The name of the property to set/replace</param>
        /// <param name="newValue">The new float value for the property</param>
        /// <returns>A new JsonElement with the updated property</returns>
        public static JsonElement SetProperty(
            this ref JsonElement? originalElement,
            string propertyName,
            float newValue)
        {
            // Check if need to set value
            if (originalElement != null && originalElement.Value.TryGetProperty(propertyName, out var propertyElement))
            {
                if (propertyElement.ValueKind == JsonValueKind.Number)
                {
                    if (propertyElement.TryGetSingle(out var existedValue))
                    {
                        if (System.Math.Abs(existedValue - newValue) < float.Epsilon)
                            return originalElement.Value; // no need to set value
                    }
                }
            }

            using var stream = new System.IO.MemoryStream();
            using var writer = new Utf8JsonWriter(stream);

            writer.WriteStartObject();

            if (originalElement == null)
            {
                // If originalElement is null, we just write the new property
                writer.WriteNumber(propertyName, newValue);
            }
            else
            {
                // Copy all existing properties except the one we're updating
                foreach (var property in originalElement.Value.EnumerateObject())
                {
                    if (property.Name != propertyName)
                    {
                        property.WriteTo(writer);
                    }
                }
                // Write the new property value
                writer.WriteNumber(propertyName, newValue);
            }

            writer.WriteEndObject();
            writer.Flush();

            // Parse and return the new JsonElement
            var correctedJson = Encoding.UTF8.GetString(stream.ToArray());
            originalElement = JsonDocument.Parse(correctedJson).RootElement;
            return originalElement.Value;
        }
    }
}
