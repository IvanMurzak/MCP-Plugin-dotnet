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
    public interface IWebhookEventCollector
    {
        void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails);
        void OnPromptRetrieved(string promptName, long responseSizeBytes);
        void OnResourceAccessed(string resourceUri, long responseSizeBytes);
        void OnAiAgentConnected(string sessionId, string? clientName, string? clientVersion, Dictionary<string, string>? metadata);
        void OnAiAgentDisconnected(string sessionId);
        void OnPluginConnected(string connectionId, string? clientName = null, string? clientVersion = null);
        void OnPluginDisconnected(string connectionId);
    }
}
