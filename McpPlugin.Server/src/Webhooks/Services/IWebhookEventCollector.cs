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
        void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails, string channel = "mcp");
        void OnPromptRetrieved(string promptName, long responseSizeBytes);
        void OnResourceAccessed(string resourceUri, long responseSizeBytes);

        // The overloads below carry the bearer token of the request being reported. An MCP session's
        // handlers run on the ExecutionContext captured when the session started, so the ambient
        // McpSessionTokenContext.CurrentToken is the token the session OPENED with; a client that
        // refreshes its OAuth access token on the same session would otherwise be reported under the
        // expired one. Null means "no per-request token known" and falls back to the ambient token.
        // Default implementations keep existing implementers compiling and loading unchanged.

        void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails, string channel, string? bearerToken)
            => OnToolCall(toolName, requestSizeBytes, responseSizeBytes, status, durationMs, errorDetails, channel);
        void OnPromptRetrieved(string promptName, long responseSizeBytes, string? bearerToken)
            => OnPromptRetrieved(promptName, responseSizeBytes);
        void OnResourceAccessed(string resourceUri, long responseSizeBytes, string? bearerToken)
            => OnResourceAccessed(resourceUri, responseSizeBytes);
        void OnAiAgentConnected(string sessionId, string? clientName, string? clientVersion, Dictionary<string, string>? metadata);
        void OnAiAgentDisconnected(string sessionId);
        void OnPluginConnected(string connectionId, string? token, string? clientName = null, string? clientVersion = null);
        void OnPluginDisconnected(string connectionId);
    }
}
