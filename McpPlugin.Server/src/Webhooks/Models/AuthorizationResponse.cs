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
    public class AuthorizationResponse
    {
        [JsonPropertyName("allowed")]
        public bool Allowed { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
