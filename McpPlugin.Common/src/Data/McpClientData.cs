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

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class McpClientData
    {
        [JsonPropertyName("isConnected")]
        public bool IsConnected { get; set; }

        [JsonPropertyName("sessionId")]
        public string? SessionId { get; set; }

        [JsonPropertyName("clientTitle")]
        public string? ClientTitle { get; set; }

        [JsonPropertyName("clientName")]
        public string? ClientName { get; set; }

        [JsonPropertyName("clientVersion")]
        public string? ClientVersion { get; set; }

        [JsonPropertyName("clientDescription")]
        public string? ClientDescription { get; set; }

        [JsonPropertyName("clientWebsiteUrl")]
        public string? ClientWebsiteUrl { get; set; }
    }
}
