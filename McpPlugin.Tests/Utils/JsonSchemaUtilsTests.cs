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
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Utils;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Model;
using FluentAssertions;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Utils
{
    public class JsonSchemaUtilsTests
    {
        [Fact]
        public void FixSerializedMemberSchema_ShouldRemoveTypeRestrictionFromValueProperty()
        {
            // Arrange
            var reflector = new Reflector();
            var types = new (System.Type type, string name, string? description, bool required)[]
            {
                (typeof(SerializedMember), "member", null, true)
            };
            var schema = reflector.JsonSchema.GenerateSchema(reflector, types, justRef: false, defines: null);

            // Verify the schema initially has the incorrect "type": "object" restriction
            var schemaObj = schema as JsonObject;
            schemaObj.Should().NotBeNull();
            var defs = schemaObj!["$defs"] as JsonObject;
            defs.Should().NotBeNull();
            var memberDef = defs!["com.IvanMurzak.ReflectorNet.Model.SerializedMember"] as JsonObject;
            memberDef.Should().NotBeNull();
            var properties = memberDef!["properties"] as JsonObject;
            properties.Should().NotBeNull();
            var valueProp = properties!["value"] as JsonObject;
            valueProp.Should().NotBeNull();
            
            // Verify initial state has the type restriction
            valueProp!.ContainsKey("type").Should().BeTrue("the original schema should have type restriction");
            valueProp["type"]?.ToString().Should().Be("object");

            // Act
            var fixedSchema = JsonSchemaUtils.FixSerializedMemberSchema(schema);

            // Assert
            var fixedSchemaObj = fixedSchema as JsonObject;
            fixedSchemaObj.Should().NotBeNull();
            var fixedDefs = fixedSchemaObj!["$defs"] as JsonObject;
            fixedDefs.Should().NotBeNull();
            var fixedMemberDef = fixedDefs!["com.IvanMurzak.ReflectorNet.Model.SerializedMember"] as JsonObject;
            fixedMemberDef.Should().NotBeNull();
            var fixedProperties = fixedMemberDef!["properties"] as JsonObject;
            fixedProperties.Should().NotBeNull();
            var fixedValueProp = fixedProperties!["value"] as JsonObject;
            fixedValueProp.Should().NotBeNull();
            
            // Verify the type restriction has been removed
            fixedValueProp!.ContainsKey("type").Should().BeFalse("the type restriction should be removed to allow any JSON type");
            
            // Verify description is still present
            fixedValueProp.ContainsKey("description").Should().BeTrue();
        }

        [Fact]
        public void FixSerializedMemberSchema_WithNullSchema_ShouldReturnNull()
        {
            // Act
            var result = JsonSchemaUtils.FixSerializedMemberSchema(null);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void FixSerializedMemberSchema_WithSchemaWithoutSerializedMember_ShouldReturnUnchanged()
        {
            // Arrange
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string" }
                }
            };

            // Act
            var result = JsonSchemaUtils.FixSerializedMemberSchema(schema);

            // Assert
            result.Should().NotBeNull();
            result.ToJsonString().Should().Be(schema.ToJsonString());
        }

        [Fact]
        public void FixSerializedMemberSchema_ShouldAllowSerializationOfDifferentValueTypes()
        {
            // Arrange
            var reflector = new Reflector();
            
            // Create SerializedMember instances with different value types
            var stringMember = new SerializedMember
            {
                name = "stringField",
                typeName = "System.String"
            };
            stringMember.valueJsonElement = JsonSerializer.SerializeToElement("Hello World");

            var intMember = new SerializedMember
            {
                name = "intField",
                typeName = "System.Int32"
            };
            intMember.valueJsonElement = JsonSerializer.SerializeToElement(42);

            var objectMember = new SerializedMember
            {
                name = "objectField",
                typeName = "System.Object"
            };
            objectMember.valueJsonElement = JsonSerializer.SerializeToElement(new { x = 1, y = 2 });

            // Act - Serialize each member
            var stringJson = reflector.JsonSerializer.Serialize(stringMember);
            var intJson = reflector.JsonSerializer.Serialize(intMember);
            var objectJson = reflector.JsonSerializer.Serialize(objectMember);

            // Assert - Verify different value types are serialized correctly
            stringJson.Should().Contain("\"value\": \"Hello World\"");
            intJson.Should().Contain("\"value\": 42");
            objectJson.Should().Contain("\"value\": {");

            // The schema should now accept all these different value types
            // (This is verified by the schema fix removing the type restriction)
        }
    }
}
