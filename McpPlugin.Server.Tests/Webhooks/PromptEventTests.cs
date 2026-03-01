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
    public class PromptEventTests
    {
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void PromptEvent_SerializesCorrectShape()
        {
            var evt = new PromptEvent
            {
                PromptName = "code-review",
                ResponseSizeBytes = 1024
            };

            var payload = new WebhookPayload<PromptEvent>
            {
                SchemaVersion = "1.0",
                EventType = "prompt.retrieved",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("1.0");
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("prompt.retrieved");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("promptName").GetString().ShouldBe("code-review");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(1024);
        }

        [Fact]
        public void PromptEvent_ResponseSizeBytes_ReflectsActualValue()
        {
            var evt = new PromptEvent
            {
                PromptName = "large-prompt",
                ResponseSizeBytes = 65536
            };

            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("responseSizeBytes").GetInt64().ShouldBe(65536);
        }
    }
}
