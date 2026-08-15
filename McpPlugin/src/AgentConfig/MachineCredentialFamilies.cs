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
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The <c>families</c> container of the v2 machine credential document (design 04 §1): one
    /// credential family per token plane on this machine.
    /// </summary>
    public sealed class MachineCredentialFamilies
    {
        /// <summary>
        /// The <c>mcp:agent</c>-scope family — the credential minted by an agent-plane login
        /// (device flow / browser). Stamped with the <c>clientId</c> it was minted under.
        /// </summary>
        [JsonPropertyName("agent")]
        public MachineCredentialFamily? Agent { get; set; }

        /// <summary>
        /// The <c>mcp:plugin</c>-scope family — derived from <see cref="Agent"/> via RFC 8693
        /// token exchange (or minted directly by a tools-only login). Its token material is also
        /// mirrored at the document's top level for pre-v2 readers.
        /// </summary>
        [JsonPropertyName("plugin")]
        public MachineCredentialFamily? Plugin { get; set; }

        /// <summary>
        /// The adopted v1 credential (present only after reading a <c>{version: 1}</c> document,
        /// design F11.1). Carries no <c>clientId</c> / <c>scope</c> — unknown by definition.
        /// </summary>
        [JsonPropertyName("legacy")]
        public MachineCredentialFamily? Legacy { get; set; }

        /// <summary>Unknown JSON fields preserved across a read→write round-trip (e.g. families added by a newer schema).</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
