/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks.Models
{
    public record AuthorizationRequest
    {
        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; init; } = "1.0";

        [JsonPropertyName("eventType")]
        public string EventType { get; init; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; init; } = string.Empty;

        [JsonPropertyName("connectionId")]
        public string ConnectionId { get; init; } = string.Empty;

        [JsonPropertyName("clientType")]
        public string ClientType { get; init; } = string.Empty;

        [JsonPropertyName("bearerToken")]
        public string? BearerToken { get; init; }

        [JsonPropertyName("remoteIpAddress")]
        public string? RemoteIpAddress { get; init; }

        [JsonPropertyName("userAgent")]
        public string? UserAgent { get; init; }

        [JsonPropertyName("requestPath")]
        public string? RequestPath { get; init; }

        [JsonPropertyName("clientName")]
        public string? ClientName { get; init; }

        [JsonPropertyName("clientVersion")]
        public string? ClientVersion { get; init; }

        [JsonPropertyName("hmacSignature")]
        public string? HmacSignature { get; init; }
    }
}
