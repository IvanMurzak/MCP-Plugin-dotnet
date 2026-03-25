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

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class PromptEvent
    {
        [JsonPropertyName("promptName")]
        public string PromptName { get; set; } = string.Empty;

        [JsonPropertyName("responseSizeBytes")]
        public long ResponseSizeBytes { get; set; }

        [JsonPropertyName("bearerToken")]
        public string? BearerToken { get; set; }

        [JsonPropertyName("clientIp")]
        public string? ClientIp { get; set; }

        [JsonPropertyName("userAgent")]
        public string? UserAgent { get; set; }
    }
}
