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
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The secret credential material persisted per machine in
    /// <see cref="MachineCredentialStore"/> (<c>~/.ai-game-dev/credentials.json</c>).
    ///
    /// <para>
    /// A single ai-game.dev account credential shared by every engine plugin, CLI, and the local
    /// server on the machine — sign-in happens once per machine, not per engine/project. Unknown
    /// JSON fields are ignored on read so the schema can grow forwards-compatibly.
    /// </para>
    ///
    /// <para><b>Never write this to a project file or VCS</b> — it lives only in the protected
    /// machine store (0600 on POSIX / DPAPI on Windows).</para>
    /// </summary>
    public sealed class MachineCredentials
    {
        /// <summary>Schema version of the persisted document (currently 1).</summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>The current short-lived ES256 JWT access token (MCP audience).</summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        /// <summary>The rotating refresh token used to mint a new access token before <see cref="ExpiresAt"/>.</summary>
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        /// <summary>Absolute expiry of <see cref="AccessToken"/>; used to schedule proactive refresh.</summary>
        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>The server target the credential was issued for (hosted <c>https://ai-game.dev</c> or a local URL). Optional.</summary>
        [JsonPropertyName("serverTarget")]
        public string? ServerTarget { get; set; }

        /// <summary>The account id (<c>sub</c>) the credential resolves to. Optional; audit/diagnostic only.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }
    }
}
