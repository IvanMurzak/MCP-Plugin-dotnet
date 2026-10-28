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
using System.Text.Json;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class ToolArgumentValidatorTests
    {
        const string Schema = """
            {"type":"object","properties":{
              "query":{"type":"string"},
              "max_targets":{"type":"integer"},
              "ratio":{"type":"number"},
              "dry_run":{"type":["boolean","null"]},
              "target":{"anyOf":[{"type":"object"},{"type":"null"}]},
              "complex":{"$ref":"#/$defs/Thing"},
              "log_type":{"type":"string","enum":["Log","Warning","Error"]},
              "label":{"type":["string","null"]}
            },"required":["query","label"]}
            """;

        static string? Validate(string argumentsJson, StringComparer? comparer = null)
        {
            var schema = JsonDocument.Parse(Schema).RootElement;
            var arguments = new Dictionary<string, JsonElement>(comparer ?? StringComparer.Ordinal);
            foreach (var property in JsonDocument.Parse(argumentsJson).RootElement.EnumerateObject())
                arguments[property.Name] = property.Value.Clone();
            return ToolArgumentValidator.Validate("rql-query", schema, arguments);
        }

        [Theory]
        [InlineData("{\"query\":\"x\",\"label\":null}")] // a required argument whose schema allows null
        [InlineData("{\"query\":{\"a\":1},\"label\":\"l\"}")] // the plugin passes an object to a string as its JSON text
        [InlineData("{\"query\":\"x\",\"label\":\"l\",\"dry_run\":1}")] // the plugin reads a boolean from a number
        [InlineData("{\"query\":\"x\",\"label\":\"l\",\"log_type\":2}")] // ... and an enum from its number
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":5}")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":\"5\"}")] // the plugin reads numbers from strings
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":5.0}")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"ratio\":0.5,\"dry_run\":true}")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"dry_run\":null,\"target\":null}")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"target\":{\"a\":1}}")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"complex\":123,\"unknown\":[1]}")] // not stated plainly: left to the plugin
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":null}")] // a null optional argument means "not given"
        public void ValidArguments_Pass(string arguments)
            => Assert.Null(Validate(arguments));

        [Fact]
        public void MissingRequired_NamesTheArgumentAndItsType()
        {
            var error = Validate("{\"label\":\"l\"}");
            Assert.Equal("[invalid_params] Invalid params for tool 'rql-query': missing required argument 'query' (a string).", error);
        }

        [Fact]
        public void NullRequired_IsMissing()
            => Assert.Contains("missing required argument 'query'", Validate("{\"query\":null}"));

        [Fact]
        public void NumberForString_IsRejected()
        {
            var error = Validate("{\"query\":123,\"label\":\"l\"}");
            Assert.Equal("[invalid_params] Invalid params for tool 'rql-query': argument 'query' must be a string, got a number (123).", error);
        }

        [Theory]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":\"abc\"}", "argument 'max_targets' must be an integer, got a string")]
        [InlineData("{\"label\":\"l\",\"query\":\"x\",\"max_targets\":1.5}", "argument 'max_targets' must be an integer, got a number (1.5)")]
        [InlineData("{\"query\":\"x\",\"dry_run\":\"maybe\"}", "argument 'dry_run' must be a boolean or null, got a string")]
        [InlineData("{\"query\":\"x\",\"log_type\":true}", "argument 'log_type' must be a string, got a boolean (true)")]
        [InlineData("{\"query\":\"x\",\"target\":7}", "argument 'target' must be an object or null, got a number (7)")]
        public void WrongType_IsRejected(string arguments, string expected)
            => Assert.Contains(expected, Validate(arguments));

        [Fact]
        public void EveryProblem_IsListed()
        {
            var error = Validate("{\"label\":\"l\",\"max_targets\":true}");
            Assert.Contains("missing required argument 'query'", error);
            Assert.Contains("argument 'max_targets' must be an integer, got a boolean (true)", error);
        }

        [Fact]
        public void ArgumentNames_MatchCaseInsensitively_LikeTheRestApi()
            => Assert.Null(Validate("{\"Query\":\"x\",\"Label\":\"l\",\"MAX_TARGETS\":3}", StringComparer.OrdinalIgnoreCase));

        [Fact]
        public void MissingRequiredArgumentNotStatedPlainly_IsLeftToThePlugin()
        {
            // ReflectorNet lists `EchoPayload? payload` (no default) as required, and the plugin accepts it missing.
            var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"payload\":{\"$ref\":\"#/$defs/P\"}},\"required\":[\"payload\",\"unlisted\"]}").RootElement;
            Assert.Null(ToolArgumentValidator.Validate("t", schema, new Dictionary<string, JsonElement>()));
            Assert.Null(ToolArgumentValidator.Validate("t", schema, new Dictionary<string, JsonElement> { ["payload"] = JsonDocument.Parse("null").RootElement }));
        }

        [Fact]
        public void NullForARequiredNullableArgument_Passes()
        {
            var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"note\":{\"type\":\"string\",\"nullable\":true}},\"required\":[\"note\"]}").RootElement;
            var arguments = new Dictionary<string, JsonElement> { ["note"] = JsonDocument.Parse("null").RootElement };
            Assert.Null(ToolArgumentValidator.Validate("t", schema, arguments));
        }

        [Fact]
        public void SchemaWithoutProperties_PassesAnything()
        {
            var schema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;
            var arguments = new Dictionary<string, JsonElement> { ["x"] = JsonDocument.Parse("1").RootElement };
            Assert.Null(ToolArgumentValidator.Validate("t", schema, arguments));
        }
    }
}
