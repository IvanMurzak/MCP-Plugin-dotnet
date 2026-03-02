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
    public sealed class ResourceEvent
    {
        [JsonPropertyName("resourceUri")]
        public string ResourceUri { get; set; } = string.Empty;

        [JsonPropertyName("responseSizeBytes")]
        public long ResponseSizeBytes { get; set; }
    }
}
