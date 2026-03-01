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
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    public class ResourceEventTests
    {
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void ResourceEvent_SerializesCorrectShape()
        {
            var evt = new ResourceEvent
            {
                ResourceUri = "file:///project/README.md",
                ResponseSizeBytes = 4096
            };

            var payload = new WebhookPayload<ResourceEvent>
            {
                SchemaVersion = "1.0",
                EventType = "resource.accessed",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("1.0");
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("resource.accessed");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("resourceUri").GetString().ShouldBe("file:///project/README.md");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(4096);
        }

        [Fact]
        public void ResourceEvent_UriTemplateMatch_SerializesCorrectly()
        {
            var evt = new ResourceEvent
            {
                ResourceUri = "template://users/{id}",
                ResponseSizeBytes = 256
            };

            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("resourceUri").GetString().ShouldBe("template://users/{id}");
            doc.RootElement.GetProperty("responseSizeBytes").GetInt64().ShouldBe(256);
        }
    }
}
