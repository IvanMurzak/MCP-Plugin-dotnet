/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// One credential family of the v2 machine credential document (design 04 §1): the token
    /// triple plus the OAuth <c>clientId</c> the family was minted under and the <c>scope</c> it
    /// carries. The legacy family (an adopted v1 credential) has neither — they are unknown by
    /// definition.
    /// </summary>
    public sealed class MachineCredentialFamily
    {
        /// <summary>The current short-lived ES256 JWT access token of this family.</summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        /// <summary>The rotating refresh token used to mint a new access token before <see cref="ExpiresAt"/>.</summary>
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        /// <summary>Absolute expiry of <see cref="AccessToken"/>; used to schedule proactive refresh.</summary>
        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>
        /// The OAuth client id this family was minted under (design D8) — written from the value
        /// actually presented at mint/derivation, never inferred. A later refresh must present
        /// exactly this id. <c>null</c> for the legacy family (unknown by definition).
        /// </summary>
        [JsonPropertyName("clientId")]
        public string? ClientId { get; set; }

        /// <summary>
        /// The OAuth scope of this family (<c>mcp:agent</c> / <c>mcp:plugin</c>). <c>null</c> for
        /// the legacy family (unknown by definition).
        /// </summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        /// <summary>Unknown JSON fields preserved across a read→write round-trip.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
