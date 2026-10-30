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
    public class RequestVersionHandshake : IRequestID
    {
        [JsonPropertyName("requestId")]
        public string RequestID { get; set; } = string.Empty;

        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = string.Empty;

        [JsonPropertyName("pluginVersion")]
        public string PluginVersion { get; set; } = string.Empty;

        [JsonPropertyName("environment")]
        public string Environment { get; set; } = string.Empty;

        /// <summary>
        /// The McpPlugin library version the plugin is built on (<see cref="Consts.PluginVersion"/>), sent
        /// since 8.14.0; <see cref="PluginVersion"/> is the host's own version (Unity-MCP 1.6.0, ...).
        /// Absent from older plugins, so the server keeps their previous behaviour (see
        /// <c>PluginArgumentPolicy</c>). Optional on the wire: an older server ignores it.
        /// </summary>
        [JsonPropertyName("mcpPluginVersion")]
        public string? McpPluginVersion { get; set; }
    }
}