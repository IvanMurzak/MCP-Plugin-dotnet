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
    public class VersionHandshakeResponse
    {
        [JsonPropertyName("apiVersion")]
        public string ApiVersion { get; set; } = string.Empty;

        [JsonPropertyName("serverVersion")]
        public string ServerVersion { get; set; } = string.Empty;

        [JsonPropertyName("compatible")]
        public bool Compatible { get; set; } = false;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// True when the handshake failed due to a connection error (e.g. the server
        /// disconnected the plugin before the handshake response arrived) rather than
        /// an actual version incompatibility. Client-side only — never sent over the wire.
        /// </summary>
        [JsonIgnore]
        public bool IsConnectionError { get; set; } = false;
    }
}