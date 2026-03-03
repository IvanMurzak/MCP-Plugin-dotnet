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
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
    public class WebhookEventCollectorTests
    {
        static WebhookOptions CreateOptions(
            string? toolUrl = "https://example.com/tools",
            string? promptUrl = "https://example.com/prompts",
            string? resourceUrl = "https://example.com/resources",
            string? connectionUrl = "https://example.com/connections",
            string? token = "test-token")
        {
            return new WebhookOptions(toolUrl, promptUrl, resourceUrl, connectionUrl, token, "X-Webhook-Token", 10000);
        }

        [Fact]
        public void OnToolCall_WhenEnabled_EnqueuesMessage()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnToolCall("add", 42, 18, "success", 150, null);

            captured.ShouldNotBeNull();
            captured!.TargetUrl.ShouldBe("https://example.com/tools");
            captured.HeaderName.ShouldBe("X-Webhook-Token");
            captured.TokenValue.ShouldBe("test-token");

            var doc = JsonDocument.Parse(captured.JsonPayload);
            doc.RootElement.GetProperty("schemaVersion").GetString().ShouldBe("1.0");
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("tool.call.completed");
            doc.RootElement.TryGetProperty("timestamp", out _).ShouldBeTrue();

            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("toolName").GetString().ShouldBe("add");
            data.GetProperty("requestSizeBytes").GetInt64().ShouldBe(42);
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(18);
            data.GetProperty("status").GetString().ShouldBe("success");
            data.GetProperty("durationMs").GetInt64().ShouldBe(150);
            data.TryGetProperty("errorDetails", out _).ShouldBeFalse(); // null omitted
        }

        [Fact]
        public void OnToolCall_WhenDisabled_DoesNotEnqueue()
        {
            var options = CreateOptions(toolUrl: null);
            var dispatcher = new Mock<IWebhookDispatcher>();

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnToolCall("add", 42, 18, "success", 150, null);

            dispatcher.Verify(d => d.TryEnqueue(It.IsAny<WebhookMessage>()), Times.Never);
        }

        [Fact]
        public void OnToolCall_WithError_IncludesErrorDetails()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnToolCall("divide", 50, 0, "failure", 10, "Division by zero");

            captured.ShouldNotBeNull();
            var doc = JsonDocument.Parse(captured!.JsonPayload);
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("status").GetString().ShouldBe("failure");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(0);
            data.GetProperty("errorDetails").GetString().ShouldBe("Division by zero");
        }

        [Fact]
        public void OnPromptRetrieved_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnPromptRetrieved("code-review", 1024);

            captured.ShouldNotBeNull();
            captured!.TargetUrl.ShouldBe("https://example.com/prompts");

            var doc = JsonDocument.Parse(captured.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("prompt.retrieved");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("promptName").GetString().ShouldBe("code-review");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(1024);
        }

        [Fact]
        public void OnResourceAccessed_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnResourceAccessed("file:///project/README.md", 4096);

            captured.ShouldNotBeNull();
            captured!.TargetUrl.ShouldBe("https://example.com/resources");

            var doc = JsonDocument.Parse(captured.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("resource.accessed");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("resourceUri").GetString().ShouldBe("file:///project/README.md");
            data.GetProperty("responseSizeBytes").GetInt64().ShouldBe(4096);
        }

        [Fact]
        public void OnAiAgentConnected_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            var metadata = new Dictionary<string, string>
            {
                { "title", "Claude" },
                { "description", "Anthropic AI Assistant" }
            };

            collector.OnAiAgentConnected("session-123", "Claude Desktop", "1.2.3", metadata);

            captured.ShouldNotBeNull();
            captured!.TargetUrl.ShouldBe("https://example.com/connections");

            var doc = JsonDocument.Parse(captured.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.ai-agent.connected");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("connected");
            data.GetProperty("clientType").GetString().ShouldBe("ai-agent");
            data.GetProperty("sessionId").GetString().ShouldBe("session-123");
            data.GetProperty("clientName").GetString().ShouldBe("Claude Desktop");
            data.GetProperty("clientVersion").GetString().ShouldBe("1.2.3");
            data.GetProperty("metadata").GetProperty("title").GetString().ShouldBe("Claude");
        }

        [Fact]
        public void OnAiAgentDisconnected_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnAiAgentDisconnected("session-123");

            captured.ShouldNotBeNull();
            var doc = JsonDocument.Parse(captured!.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.ai-agent.disconnected");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("disconnected");
            data.GetProperty("clientType").GetString().ShouldBe("ai-agent");
            data.GetProperty("sessionId").GetString().ShouldBe("session-123");
        }

        [Fact]
        public void OnPluginConnected_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnPluginConnected("conn-abc", "test-token", "MyUnityApp", "2.0.0");

            captured.ShouldNotBeNull();
            var doc = JsonDocument.Parse(captured!.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.plugin.connected");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("connected");
            data.GetProperty("clientType").GetString().ShouldBe("plugin");
            data.GetProperty("sessionId").GetString().ShouldBe("conn-abc");
            data.GetProperty("clientName").GetString().ShouldBe("MyUnityApp");
            data.GetProperty("clientVersion").GetString().ShouldBe("2.0.0");
        }

        [Fact]
        public void OnPluginDisconnected_WhenEnabled_EnqueuesCorrectPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            // Must register a successful handshake first
            collector.OnPluginConnected("conn-abc", null);
            captured = null; // reset to capture only the disconnect message

            collector.OnPluginDisconnected("conn-abc");

            captured.ShouldNotBeNull();
            var doc = JsonDocument.Parse(captured!.JsonPayload);
            doc.RootElement.GetProperty("eventType").GetString().ShouldBe("connection.plugin.disconnected");
            var data = doc.RootElement.GetProperty("data");
            data.GetProperty("eventType").GetString().ShouldBe("disconnected");
            data.GetProperty("clientType").GetString().ShouldBe("plugin");
        }

        [Fact]
        public void WhenNoToken_OmitsHeaderAndTokenFromMessage()
        {
            var options = new WebhookOptions("https://example.com/tools", null, null, null, null, null, 10000);
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnToolCall("test", 10, 20, "success", 5, null);

            captured.ShouldNotBeNull();
            captured!.HeaderName.ShouldBeNull();
            captured.TokenValue.ShouldBeNull();
        }

        [Fact]
        public void OnAiAgentConnected_WithEmptyMetadata_OmitsMetadataFromPayload()
        {
            var options = CreateOptions();
            var dispatcher = new Mock<IWebhookDispatcher>();
            WebhookMessage? captured = null;
            dispatcher.Setup(d => d.TryEnqueue(It.IsAny<WebhookMessage>()))
                .Callback<WebhookMessage>(m => captured = m)
                .Returns(true);

            var collector = new WebhookEventCollector(
                Mock.Of<ILogger<WebhookEventCollector>>(),
                dispatcher.Object,
                options);

            collector.OnAiAgentConnected("session-123", null, null, new Dictionary<string, string>());

            captured.ShouldNotBeNull();
            var doc = JsonDocument.Parse(captured!.JsonPayload);
            var data = doc.RootElement.GetProperty("data");
            data.TryGetProperty("metadata", out _).ShouldBeFalse(); // empty metadata omitted
            data.TryGetProperty("clientName", out _).ShouldBeFalse(); // null omitted
            data.TryGetProperty("clientVersion", out _).ShouldBeFalse(); // null omitted
        }
    }
}
