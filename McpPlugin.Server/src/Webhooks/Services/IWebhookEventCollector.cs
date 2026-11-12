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
        void OnAiAgentConnected(string sessionId, string? clientName, string? clientVersion, Dictionary<string, string>? metadata);
        void OnAiAgentDisconnected(string sessionId);
        void OnPluginConnected(string connectionId, string? token, string? clientName = null, string? clientVersion = null);
        void OnPluginDisconnected(string connectionId);

        /// <summary>
        /// Reports a tool call with the bearer token of the request that made it.
        /// </summary>
        /// <param name="bearerToken">
        /// The token of the request being reported, or null/empty to use the session's ambient
        /// token. An MCP session's handlers run on the ExecutionContext captured when the session
        /// started, so the ambient token is the one the session OPENED with; after an OAuth refresh
        /// on the same session only the per-request token is current. The default implementation
        /// ignores it and calls the overload without a token, so existing implementers keep working.
        /// </param>
        void OnToolCall(string toolName, long requestSizeBytes, long responseSizeBytes, string status, long durationMs, string? errorDetails, string channel, string? bearerToken)
            => OnToolCall(toolName, requestSizeBytes, responseSizeBytes, status, durationMs, errorDetails, channel);

        /// <summary>Reports a retrieved prompt with the bearer token of the request that made it.</summary>
        /// <param name="bearerToken">As for <see cref="OnToolCall(string, long, long, string, long, string?, string, string?)"/>.</param>
        void OnPromptRetrieved(string promptName, long responseSizeBytes, string? bearerToken)
            => OnPromptRetrieved(promptName, responseSizeBytes);

        /// <summary>Reports a resource read with the bearer token of the request that made it.</summary>
        /// <param name="bearerToken">As for <see cref="OnToolCall(string, long, long, string, long, string?, string, string?)"/>.</param>
        void OnResourceAccessed(string resourceUri, long responseSizeBytes, string? bearerToken)
            => OnResourceAccessed(resourceUri, responseSizeBytes);
    }
}
