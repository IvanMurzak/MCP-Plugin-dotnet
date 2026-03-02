/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
    public class ConnectionEventTests
    {
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void AiAgentConnected_SerializesCorrectShape()
        {
            var evt = new ConnectionEvent
            {
                EventType = "connected",
                ClientType = "ai-agent",
                SessionId = "550e8400-e29b-41d4-a716-446655440000",
                ClientName = "Claude Desktop",
                ClientVersion = "1.2.3",
                Metadata = new Dictionary<string, string>
                {
                    { "title", "Claude" },
                    { "description", "Anthropic AI Assistant" }
                }
            };

            var payload = new WebhookPayload<ConnectionEvent>
            {
                SchemaVersion = "1.0",
                EventType = "connection.ai-agent.connected",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.ai-agent.connected");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("connected");
            data.GetProperty("clientType").GetString().ShouldBe("ai-agent");
            data.GetProperty("sessionId").GetString().ShouldBe("550e8400-e29b-41d4-a716-446655440000");
            data.GetProperty("clientName").GetString().ShouldBe("Claude Desktop");
            data.GetProperty("clientVersion").GetString().ShouldBe("1.2.3");
            data.GetProperty("metadata").GetProperty("title").GetString().ShouldBe("Claude");
        }

        [Fact]
        public void AiAgentDisconnected_SerializesCorrectShape()
        {
            var evt = new ConnectionEvent
            {
                EventType = "disconnected",
                ClientType = "ai-agent",
                SessionId = "550e8400-e29b-41d4-a716-446655440000"
            };

            var payload = new WebhookPayload<ConnectionEvent>
            {
                SchemaVersion = "1.0",
                EventType = "connection.ai-agent.disconnected",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.ai-agent.disconnected");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("disconnected");
            data.GetProperty("clientType").GetString().ShouldBe("ai-agent");
            data.GetProperty("sessionId").GetString().ShouldBe("550e8400-e29b-41d4-a716-446655440000");
            data.TryGetProperty("clientName", out _).ShouldBeFalse();
            data.TryGetProperty("clientVersion", out _).ShouldBeFalse();
            data.TryGetProperty("metadata", out _).ShouldBeFalse();
        }

        [Fact]
        public void PluginConnected_SerializesCorrectShape()
        {
            var evt = new ConnectionEvent
            {
                EventType = "connected",
                ClientType = "plugin",
                SessionId = "abc123-signalr-connection-id",
                ClientName = "MyUnityApp",
                ClientVersion = "2.0.0"
            };

            var payload = new WebhookPayload<ConnectionEvent>
            {
                SchemaVersion = "1.0",
                EventType = "connection.plugin.connected",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.plugin.connected");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("connected");
            data.GetProperty("clientType").GetString().ShouldBe("plugin");
            data.GetProperty("sessionId").GetString().ShouldBe("abc123-signalr-connection-id");
            data.GetProperty("clientName").GetString().ShouldBe("MyUnityApp");
        }

        [Fact]
        public void PluginDisconnected_SerializesCorrectShape()
        {
            var evt = new ConnectionEvent
            {
                EventType = "disconnected",
                ClientType = "plugin",
                SessionId = "abc123-signalr-connection-id"
            };

            var payload = new WebhookPayload<ConnectionEvent>
            {
                SchemaVersion = "1.0",
                EventType = "connection.plugin.disconnected",
                Timestamp = System.DateTimeOffset.UtcNow,
                Data = evt
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var doc = JsonDocument.Parse(json);

            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.plugin.disconnected");

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("disconnected");
            data.GetProperty("clientType").GetString().ShouldBe("plugin");
            data.TryGetProperty("metadata", out _).ShouldBeFalse();
        }

        [Fact]
        public void MetadataOmitted_WhenNull()
        {
            var evt = new ConnectionEvent
            {
                EventType = "connected",
                ClientType = "ai-agent",
                SessionId = "session-1",
                Metadata = null
            };

            var json = JsonSerializer.Serialize(evt, JsonOptions);
            var doc = JsonDocument.Parse(json);
            doc.RootElement.TryGetProperty("metadata", out _).ShouldBeFalse();
        }
    }
}
