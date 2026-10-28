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

namespace com.IvanMurzak.McpPlugin.Common
{
    /// <summary>
    /// What an engine plugin connection is. The server keeps one connection per role side by side and
    /// routes agents to the editor; a runtime serves only when no editor is there (a standalone game).
    /// </summary>
    public enum ClientRole
    {
        /// <summary>The engine editor (the default).</summary>
        Editor = 0,

        /// <summary>A running game: a player build or the editor's play mode in a separate process.</summary>
        Runtime = 1,
    }

    /// <summary>Wire form of <see cref="ClientRole"/> (hub query <c>client_role</c> / header <c>X-Client-Role</c>).</summary>
    public static class ClientRoles
    {
        /// <summary>
        /// Version-handshake <c>environment</c> suffix that marks a runtime. The role fallback for a plugin
        /// that does not send <c>client_role</c> (the Godot runtime sends <c>Godot 4.5 (runtime)</c>).
        /// </summary>
        public const string RuntimeEnvironmentSuffix = "(runtime)";

        /// <summary>The value sent on the wire: <c>editor</c> or <c>runtime</c>.</summary>
        public static string ToWire(ClientRole role) => role == ClientRole.Runtime ? "runtime" : "editor";

        /// <summary>
        /// Parses a declared role, case-insensitively: <c>editor</c>; <c>runtime</c>, <c>game</c> or
        /// <c>player</c>. Null for an absent or unrecognised value.
        /// </summary>
        public static ClientRole? Parse(string? value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "editor": return ClientRole.Editor;
                case "runtime":
                case "game":
                case "player": return ClientRole.Runtime;
                default: return null;
            }
        }

        /// <summary>The role a version-handshake <c>environment</c> implies: runtime when it ends in <see cref="RuntimeEnvironmentSuffix"/>.</summary>
        public static ClientRole FromEnvironment(string? environment)
            => environment != null && environment.TrimEnd().EndsWith(RuntimeEnvironmentSuffix, StringComparison.OrdinalIgnoreCase)
                ? ClientRole.Runtime
                : ClientRole.Editor;
    }
}
