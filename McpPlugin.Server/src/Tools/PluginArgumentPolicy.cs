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
using System.Collections.Concurrent;
using System.Globalization;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// Which plugin connections get the server-side "missing required argument" check.
    /// <para>
    /// A plugin built on McpPlugin before 8.14.0 (ReflectorNet before 5.5.1) lists every parameter without a
    /// default as <c>required</c>, while its binder fills an omitted one with null/0, so its tools ran with
    /// such arguments left out. Only a connection whose handshake reports <c>mcpPluginVersion</c> 8.14.0 or
    /// newer is held to its <c>required</c> list; an older or unknown plugin keeps its previous behaviour
    /// (the type checks still apply to everyone). Keyed by SignalR connection id: recorded at the accepted
    /// version handshake, forgotten when the connection closes.
    /// </para>
    /// </summary>
    public static class PluginArgumentPolicy
    {
        /// <summary>The first McpPlugin version whose <c>required</c> lists match what its binder needs.</summary>
        public static readonly System.Version FirstEnforcedVersion = new System.Version(8, 14, 0);

        static readonly ConcurrentDictionary<string, bool> Enforced = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Records the McpPlugin version a connection's handshake reported (null when it reported none).</summary>
        public static void Record(string connectionId, string? mcpPluginVersion)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;
            Enforced[connectionId] = EnforcesRequired(mcpPluginVersion);
        }

        /// <summary>Forgets a closed connection.</summary>
        public static void Forget(string connectionId)
        {
            if (!string.IsNullOrEmpty(connectionId))
                Enforced.TryRemove(connectionId, out _);
        }

        /// <summary>
        /// Whether calls to <paramref name="connectionId"/> get the missing-required check: only after a handshake
        /// that reported McpPlugin 8.14.0 or newer. An unknown connection keeps the lenient (previous) behaviour.
        /// </summary>
        public static bool IsEnforced(string? connectionId)
            => connectionId != null && Enforced.TryGetValue(connectionId, out var enforced) && enforced;

        /// <summary>
        /// True when <paramref name="mcpPluginVersion"/> is 8.14.0 or newer. A pre-release or build suffix is
        /// ignored (<c>8.14.0-dev.x</c> counts as 8.14.0); null, empty or unparsable is false.
        /// </summary>
        public static bool EnforcesRequired(string? mcpPluginVersion)
        {
            if (string.IsNullOrWhiteSpace(mcpPluginVersion))
                return false;
            var text = mcpPluginVersion!.Trim();
            var cut = text.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0)
                text = text.Substring(0, cut);
            var parts = text.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                return false;
            var numbers = new int[3];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    return false;
                if (i < 3)
                    numbers[i] = n;
            }
            return new System.Version(numbers[0], numbers[1], numbers[2]) >= FirstEnforcedVersion;
        }
    }
}
