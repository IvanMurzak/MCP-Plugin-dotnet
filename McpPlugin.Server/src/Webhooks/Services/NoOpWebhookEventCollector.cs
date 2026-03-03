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

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class NoOpWebhookEventCollector : IWebhookEventCollector
    {
        public void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails) { }
        public void OnPromptRetrieved(string promptName, long responseSizeBytes) { }
        public void OnResourceAccessed(string resourceUri, long responseSizeBytes) { }
        public void OnAiAgentConnected(string sessionId, string? clientName, string? clientVersion, Dictionary<string, string>? metadata) { }
        public void OnAiAgentDisconnected(string sessionId) { }
        public void OnPluginConnected(string connectionId, string? token, string? clientName = null, string? clientVersion = null) { }
        public void OnPluginDisconnected(string connectionId) { }
    }
}
