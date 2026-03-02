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
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
    public class ToolCallEventTests
    {
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void ToolCallEvent_Success_SerializesCorrectShape()
        {
            var evt = new ToolCallEvent
            {
                ToolName = "add",
                RequestSizeBytes = 42,
                ResponseSizeBytes = 18,
                Status = "success",
                DurationMs = 150,
                ErrorDetails = null
            };

            var payload = new WebhookPayload<ToolCallEvent>
            {
                SchemaVersion = "1.0",
                EventType = "tool.call.completed",
                Timestamp = System.DateTimeOffset.Parse("2026-03-01T12:34:56.789+00:00"),
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("1.0");
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("tool.call.completed");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("toolName").GetString().ShouldBe("add");
            data.GetProperty("requestSizeBytes").GetInt64().ShouldBe(42);
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(18);
            data.GetProperty("status").GetString().ShouldBe("success");
            data.GetProperty("durationMs").GetInt64().ShouldBe(150);
            data.TryGetProperty("errorDetails", out _).ShouldBeFalse();
        }

        [Fact]
        public void ToolCallEvent_Failure_IncludesErrorDetails()
        {
            var evt = new ToolCallEvent
            {
                ToolName = "divide",
                RequestSizeBytes = 50,
                ResponseSizeBytes = 0,
                Status = "failure",
                DurationMs = 10,
                ErrorDetails = "Division by zero"
            };

            var payload = new WebhookPayload<ToolCallEvent>
            {
                SchemaVersion = "1.0",
                EventType = "tool.call.completed",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("status").GetString().ShouldBe("failure");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(0);
            data.GetProperty("errorDetails").GetString().ShouldBe("Division by zero");
        }

        [Fact]
        public void ToolCallEvent_DurationMs_ReflectsActualValue()
        {
            var evt = new ToolCallEvent
            {
                ToolName = "slow-tool",
                RequestSizeBytes = 100,
                ResponseSizeBytes = 500,
                Status = "success",
                DurationMs = 5432,
                ErrorDetails = null
            };

            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("durationMs").GetInt64().ShouldBe(5432);
        }

        [Fact]
        public void ToolCallEvent_ZeroResponseSizeBytes_OnFailure()
        {
            var evt = new ToolCallEvent
            {
                ToolName = "broken-tool",
                RequestSizeBytes = 200,
                ResponseSizeBytes = 0,
                Status = "failure",
                DurationMs = 5,
                ErrorDetails = "Tool threw exception"
            };

            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.GetProperty("responseSizeBytes").GetInt64().ShouldBe(0);
        }
    }
}
