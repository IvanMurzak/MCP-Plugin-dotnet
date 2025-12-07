/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text.Json.Nodes;

namespace com.IvanMurzak.McpPlugin.Utils
{
    /// <summary>
    /// Utility methods for JSON Schema post-processing.
    /// </summary>
    public static class JsonSchemaUtils
    {
        /// <summary>
        /// Fixes JSON Schema for SerializedMember types by removing the "type": "object" restriction
        /// from the "value" property, allowing it to accept any JSON type.
        /// 
        /// SerializedMember.value can be any JSON type (string, number, boolean, object, array, null),
        /// but the generated schema incorrectly restricts it to "object" only.
        /// </summary>
        /// <param name="schema">The JSON Schema to fix. Can be null.</param>
        /// <returns>The fixed schema, or null if input was null.</returns>
        public static JsonNode? FixSerializedMemberSchema(JsonNode? schema)
        {
            if (schema == null)
                return null;

            // Look for $defs section containing SerializedMember definitions
            if (schema is JsonObject schemaObj && schemaObj["$defs"] is JsonObject defs)
            {
                FixSerializedMemberInDefs(defs);
            }

            return schema;
        }

        private static void FixSerializedMemberInDefs(JsonObject defs)
        {
            const string SerializedMemberTypeName = "com.IvanMurzak.ReflectorNet.Model.SerializedMember";

            // Find the SerializedMember definition
            if (defs[SerializedMemberTypeName] is JsonObject memberDef)
            {
                // Fix the "value" property in this definition
                if (memberDef["properties"] is JsonObject properties &&
                    properties["value"] is JsonObject valueProp)
                {
                    // Remove the "type": "object" restriction to allow any JSON type
                    valueProp.Remove("type");
                }
            }
        }
    }
}
