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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class WebhookEventCollector : IWebhookEventCollector
    {
        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        readonly ILogger<WebhookEventCollector> _logger;
        readonly IWebhookDispatcher _dispatcher;
        readonly WebhookOptions _options;
        readonly ConcurrentDictionary<string, byte> _handshakeCompletedConnections = new();

        public WebhookEventCollector(
            ILogger<WebhookEventCollector> logger,
            IWebhookDispatcher dispatcher,
            WebhookOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails, string channel = "mcp")
        {
            if (!_options.IsToolEnabled)
                return;

            var evt = new ToolCallEvent
            {
                ToolName = toolName,
                RequestSizeBytes = requestSizeBytes,
                ResponseSizeBytes = responseSizeBytes,
                Status = status,
                DurationMs = durationMs,
                ErrorDetails = errorDetails,
                BearerToken = McpSessionTokenContext.CurrentToken,
                ClientIp = McpSessionTokenContext.CurrentClientIp,
                UserAgent = McpSessionTokenContext.CurrentUserAgent,
                Channel = channel
            };

            Enqueue(_options.ToolWebhookUrl!, "tool.call.completed", evt);
        }

        public void OnPromptRetrieved(string promptName, long responseSizeBytes)
        {
            if (!_options.IsPromptEnabled)
                return;

            var evt = new PromptEvent
            {
                PromptName = promptName,
                ResponseSizeBytes = responseSizeBytes,
                BearerToken = McpSessionTokenContext.CurrentToken,
                ClientIp = McpSessionTokenContext.CurrentClientIp,
                UserAgent = McpSessionTokenContext.CurrentUserAgent
            };

            Enqueue(_options.PromptWebhookUrl!, "prompt.retrieved", evt);
        }

        public void OnResourceAccessed(string resourceUri, long responseSizeBytes)
        {
            if (!_options.IsResourceEnabled)
                return;

            var evt = new ResourceEvent
            {
                ResourceUri = resourceUri,
                ResponseSizeBytes = responseSizeBytes,
                BearerToken = McpSessionTokenContext.CurrentToken,
                ClientIp = McpSessionTokenContext.CurrentClientIp,
                UserAgent = McpSessionTokenContext.CurrentUserAgent
            };

            Enqueue(_options.ResourceWebhookUrl!, "resource.accessed", evt);
        }

        public void OnAiAgentConnected(string sessionId, string? clientName, string? clientVersion, Dictionary<string, string>? metadata)
        {
            if (!_options.IsConnectionEnabled)
                return;

            var evt = new ConnectionEvent
            {
                EventType = "connected",
                ClientType = "ai-agent",
                SessionId = sessionId,
                ClientName = clientName,
                ClientVersion = clientVersion,
                Metadata = metadata?.Count > 0 ? metadata : null
            };

            Enqueue(_options.ConnectionWebhookUrl!, "connection.ai-agent.connected", evt);
        }

        public void OnAiAgentDisconnected(string sessionId)
        {
            if (!_options.IsConnectionEnabled)
                return;

            var evt = new ConnectionEvent
            {
                EventType = "disconnected",
                ClientType = "ai-agent",
                SessionId = sessionId
            };

            Enqueue(_options.ConnectionWebhookUrl!, "connection.ai-agent.disconnected", evt);
        }

        public void OnPluginConnected(string connectionId, string? token, string? clientName = null, string? clientVersion = null)
        {
            _handshakeCompletedConnections.TryAdd(connectionId, 0);

            if (!_options.IsConnectionEnabled)
                return;

            var evt = new ConnectionEvent
            {
                EventType = "connected",
                ClientType = "plugin",
                SessionId = connectionId,
                ClientName = clientName,
                ClientVersion = clientVersion,
                BearerToken = token
            };

            Enqueue(_options.ConnectionWebhookUrl!, "connection.plugin.connected", evt);
        }

        public void OnPluginDisconnected(string connectionId)
        {
            if (!_handshakeCompletedConnections.TryRemove(connectionId, out _))
                return;

            if (!_options.IsConnectionEnabled)
                return;

            var evt = new ConnectionEvent
            {
                EventType = "disconnected",
                ClientType = "plugin",
                SessionId = connectionId
            };

            Enqueue(_options.ConnectionWebhookUrl!, "connection.plugin.disconnected", evt);
        }

        void Enqueue<T>(string targetUrl, string eventType, T data)
        {
            var payload = new WebhookPayload<T>
            {
                SchemaVersion = "1.0",
                EventType = eventType,
                Timestamp = DateTimeOffset.UtcNow,
                Data = data
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);

            var message = new WebhookMessage(
                TargetUrl: targetUrl,
                JsonPayload: json,
                HeaderName: _options.HasToken ? _options.HeaderName : null,
                TokenValue: _options.HasToken ? _options.TokenValue : null
            );

            if (!_dispatcher.TryEnqueue(message))
            {
                _logger.LogWarning("Webhook channel full, dropping message for {EventType}.", eventType);
            }
        }
    }
}
