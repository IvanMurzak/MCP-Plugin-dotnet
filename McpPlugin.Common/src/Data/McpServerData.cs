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
    public class McpServerData
    {
        [JsonPropertyName("serverVersion")]
        public string? ServerVersion { get; set; }

        [JsonPropertyName("serverApiVersion")]
        public string? ServerApiVersion { get; set; }

        [JsonPropertyName("serverTransport")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Consts.MCP.Server.TransportMethod ServerTransport { get; set; }

        [JsonPropertyName("isAiAgentConnected")]
        public bool IsAiAgentConnected { get; set; }
    }
}
