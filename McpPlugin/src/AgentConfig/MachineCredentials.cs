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
    /// The secret credential material persisted per machine in
    /// <see cref="MachineCredentialStore"/> (<c>~/.ai-game-dev/credentials.json</c>).
    ///
    /// <para>
    /// A single ai-game.dev account credential shared by every engine plugin, CLI, and the local
    /// server on the machine — sign-in happens once per machine, not per engine/project.
    /// </para>
    ///
    /// <para><b>Schema v2 (store contract, design 04 §1).</b> Token material lives in
    /// <see cref="Families"/> (<c>agent</c> / <c>plugin</c> / <c>legacy</c>), each family carrying
    /// its own <c>clientId</c> and <c>scope</c>. The top-level
    /// <see cref="AccessToken"/> / <see cref="RefreshToken"/> / <see cref="ExpiresAt"/> fields are a
    /// <b>v1 compat mirror of the plugin family</b> (transition releases only): every v2 write
    /// re-stamps them from <c>families.plugin</c> (falling back to <c>families.legacy</c> when no
    /// plugin family exists) so pre-v2 readers, which key on the top-level fields, keep working.
    /// A <c>{version: 1}</c> document is interpreted as <c>families.legacy</c> on read and upgraded
    /// to v2 (+mirror) on its first write.</para>
    ///
    /// <para><b>Unknown JSON fields are preserved</b> across a read→write round-trip
    /// (<see cref="ExtensionData"/>, <c>[JsonExtensionData]</c>) so a newer schema is never
    /// destroyed by an older writer. Documents with <see cref="Version"/> above
    /// <see cref="CurrentVersion"/> keep their version stamp on write (version passthrough).</para>
    ///
    /// <para><b>Never write this to a project file or VCS</b> — it lives only in the protected
    /// machine store (0600 on POSIX / DPAPI on Windows).</para>
    /// </summary>
    public sealed class MachineCredentials
    {
        /// <summary>The schema version written by this codec (v2 families schema).</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// Schema version of the persisted document. Defaults to <see cref="CurrentVersion"/> for
        /// freshly constructed credentials; reflects the on-disk value after a read.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        /// <summary>
        /// v1 compat mirror: the plugin-plane access token (short-lived ES256 JWT, MCP audience).
        /// Re-stamped from <see cref="MachineCredentialFamilies.Plugin"/> (or
        /// <see cref="MachineCredentialFamilies.Legacy"/>) on every write — mutate the family, not
        /// this mirror.
        /// </summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        /// <summary>v1 compat mirror: the plugin-plane rotating refresh token (see <see cref="AccessToken"/>).</summary>
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        /// <summary>v1 compat mirror: absolute expiry of the plugin-plane access token (see <see cref="AccessToken"/>).</summary>
        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; set; }

        /// <summary>The server target the credential was issued for (hosted <c>https://ai-game.dev</c> or a local URL). Optional.</summary>
        [JsonPropertyName("serverTarget")]
        public string? ServerTarget { get; set; }

        /// <summary>The account id (<c>sub</c>) the credential resolves to. Optional; audit/diagnostic only.</summary>
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        /// <summary>
        /// The v2 per-family token material (<c>agent</c> / <c>plugin</c> / <c>legacy</c>).
        /// <c>null</c> on a pure v1 document until it is adopted (read) / upgraded (write).
        /// </summary>
        [JsonPropertyName("families")]
        public MachineCredentialFamilies? Families { get; set; }

        /// <summary>
        /// Unknown JSON fields captured on read and re-emitted on write, so a document written by a
        /// newer schema survives a round-trip through this codec unharmed.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>
        /// v1 read-compat (design 04 §1): when the document carries top-level token material but no
        /// <see cref="Families"/>, interpret the top-level fields as <c>families.legacy</c> — a
        /// credential whose <c>clientId</c> / <c>scope</c> are unknown by definition. Idempotent;
        /// no-op on documents that already carry families.
        /// </summary>
        internal void AdoptLegacyTokens()
        {
            if (Families != null)
                return;
            if (AccessToken == null && RefreshToken == null && ExpiresAt == null)
                return;

            Families = new MachineCredentialFamilies
            {
                Legacy = new MachineCredentialFamily
                {
                    AccessToken = AccessToken,
                    RefreshToken = RefreshToken,
                    ExpiresAt = ExpiresAt,
                },
            };
        }

        /// <summary>
        /// The family whose token material pre-v2 readers must see at the top level: the plugin
        /// family, falling back to the legacy family when no plugin family exists (a v1-adopted
        /// document's only plugin-plane credential). <c>null</c> for an agent-only document.
        /// </summary>
        internal MachineCredentialFamily? MirrorSource => Families?.Plugin ?? Families?.Legacy;

        /// <summary>
        /// Normalize the document to the v2 write contract (design 04 §1), in order:
        /// v1 adoption (<see cref="AdoptLegacyTokens"/>), version upgrade
        /// (<see cref="Version"/> becomes at least <see cref="CurrentVersion"/>; a newer version is
        /// passed through), then the v1 compat mirror — the top-level token fields are re-stamped
        /// from <see cref="MirrorSource"/> (cleared when the document has families but no
        /// plugin-plane family, so a stale mirror never outlives its source).
        /// Called by <see cref="MachineCredentialStore.Write"/> on every persist.
        /// </summary>
        internal void NormalizeForWrite()
        {
            AdoptLegacyTokens();

            if (Version < CurrentVersion)
                Version = CurrentVersion;

            var mirror = MirrorSource;
            AccessToken = mirror?.AccessToken;
            RefreshToken = mirror?.RefreshToken;
            ExpiresAt = mirror?.ExpiresAt;
        }
    }
}
