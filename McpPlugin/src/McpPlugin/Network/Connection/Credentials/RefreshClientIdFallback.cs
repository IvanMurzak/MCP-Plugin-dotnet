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

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The client-id fallback for credential families that store NO <c>clientId</c> (04 §3 rule 7,
    /// the twin of AI-Game-Dev-CLI-Core <c>FIRST_PARTY_REFRESH_CLIENT_IDS</c>). Such families exist
    /// in the field: a v1 store rewritten by an old Unity plugin (0.84–0.87) drops
    /// <c>families.*</c>, and pre-a6 enroll credentials were stored without the id. The token may
    /// have been minted under any first-party client, and the authorization server refuses it with
    /// <c>invalid_grant</c> / <see cref="ClientIdMismatchDescription"/> when another id is presented —
    /// while the token stays LIVE for its real mint client (the server checks the id only after
    /// liveness and writes nothing). So a mismatch is never a dead family; the provider presents
    /// the other first-party ids instead and records the one that works.
    /// </summary>
    public static class RefreshClientIdFallback
    {
        /// <summary>The authorization server's <c>error_description</c> for a refresh presented under the wrong client id.</summary>
        public const string ClientIdMismatchDescription = "client_id does not match the refresh token";

        /// <summary>The synthetic client id the enroll redeem mints plugin credentials under.</summary>
        public const string EnrollClientId = "agd_enroll";

        /// <summary>
        /// Every first-party OAuth client id a client-less family may have been minted under, in the
        /// SAME order as cli-core: each engine's plugin then CLI id (Unity, Unreal, Godot), the BRUTAL
        /// CLI, the enroll redeem's <see cref="EnrollClientId"/>, and the App. The default for
        /// <see cref="PluginCredentialProvider"/>'s fallback list.
        /// </summary>
        public static IReadOnlyList<string> DefaultFirstPartyClientIds { get; } = new[]
        {
            "unity-mcp-plugin",
            "unity-mcp-cli",
            "unreal-mcp-plugin",
            "unreal-mcp-cli",
            "godot-mcp-plugin",
            "godot-cli",
            "brutal-cli",
            EnrollClientId,
            "ai-game-dev-app",
        };

        // Plugin id ↔ CLI id of the same engine. The collapsed-v1 case is a store rewritten by one
        // engine's plugin and refreshed by that engine's CLI (or vice versa), so the sibling goes first.
        static readonly (string Plugin, string Cli)[] SiblingPairs =
        {
            ("unity-mcp-plugin", "unity-mcp-cli"),
            ("unreal-mcp-plugin", "unreal-mcp-cli"),
            ("godot-mcp-plugin", "godot-cli"),
            ("brutal-mcp-plugin", "brutal-cli"),
        };

        /// <summary>
        /// True when an error text names a client-id mismatch — the same case-insensitive
        /// <c>/client_id does not match/</c> test cli-core's <c>isClientIdMismatchText</c> uses, so
        /// both implementations classify a server answer (and a recorded sidecar reason) alike.
        /// </summary>
        internal static bool IsClientIdMismatchText(string? text)
            => text != null && text.IndexOf("client_id does not match", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// True when a refresh failed ONLY because the presented client id is not the token's mint
        /// client: <see cref="TokenRefreshFailureKind.ClientIdMismatch"/>, or — for an engine
        /// refresher that predates that kind — an <c>invalid_grant</c> whose reason names the mismatch.
        /// </summary>
        internal static bool IsClientIdMismatch(TokenRefreshResult? result)
            => result != null
                && !result.Succeeded
                && (result.FailureKind == TokenRefreshFailureKind.ClientIdMismatch
                    || (result.FailureKind == TokenRefreshFailureKind.InvalidGrant && IsClientIdMismatchText(result.FailureReason)));

        /// <summary>
        /// The fallback order (cli-core <c>orderFallbackClientIds</c>): the default id's sibling of
        /// the same engine, then <see cref="EnrollClientId"/>, then the rest of
        /// <paramref name="ids"/> in order — only ids from <paramref name="ids"/>, deduplicated,
        /// never blank and never <paramref name="defaultClientId"/> itself (it was presented first).
        /// </summary>
        internal static IReadOnlyList<string> Order(IEnumerable<string> ids, string? defaultClientId)
        {
            var given = new List<string>();
            foreach (var id in ids)
            {
                var trimmed = id?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !given.Contains(trimmed!))
                    given.Add(trimmed!);
            }

            var ordered = new List<string>(given.Count);
            void Take(string? id)
            {
                if (id != null && given.Contains(id) && !ordered.Contains(id) && id != defaultClientId)
                    ordered.Add(id);
            }

            Take(SiblingOf(defaultClientId));
            Take(EnrollClientId);
            foreach (var id in given)
                Take(id);
            return ordered;
        }

        static string? SiblingOf(string? clientId)
        {
            foreach (var (plugin, cli) in SiblingPairs)
            {
                if (clientId == plugin)
                    return cli;
                if (clientId == cli)
                    return plugin;
            }
            return null;
        }
    }
}
