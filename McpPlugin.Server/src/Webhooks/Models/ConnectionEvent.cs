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
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class ConnectionEvent
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("clientType")]
        public string ClientType { get; set; } = string.Empty;

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("clientName")]
        public string? ClientName { get; set; }

        [JsonPropertyName("clientVersion")]
        public string? ClientVersion { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }

        [JsonPropertyName("bearerToken")]
        public string? BearerToken { get; set; }
    }
}
