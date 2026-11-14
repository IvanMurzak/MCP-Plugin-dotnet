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
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The project a plugin connection of a single-project strategy (<c>none</c>, <c>token</c>) serves: the
    /// project-path hashes its hub handshake query carries (<see cref="Consts.MCP.Server.HubQuery.ProjectPathHash"/>,
    /// <see cref="Consts.MCP.Server.HubQuery.ProjectPathHashLegacy"/>), which a session pin matches by the same rule
    /// as <see cref="PluginInstance.MatchesPin"/>.
    /// </summary>
    public sealed record PluginProject(string ProjectPathHash, string ProjectPathHashLegacy = "")
    {
        /// <summary>True when <paramref name="pin"/> is a prefix of either hash.</summary>
        public bool MatchesPin(string? pin) => PluginInstance.PinMatches(pin, ProjectPathHash, ProjectPathHashLegacy);

        /// <summary>
        /// True unless the two name different projects: compared by the v2 hash when both have one, else by the legacy
        /// hash; a project whose hashes cannot be compared is not taken for another (a plugin that added the legacy hash
        /// on upgrade is still the same project).
        /// </summary>
        public bool IsSameProject(PluginProject other)
        {
            if (ProjectPathHash.Length > 0 && other.ProjectPathHash.Length > 0)
                return string.Equals(ProjectPathHash, other.ProjectPathHash, StringComparison.OrdinalIgnoreCase);
            if (ProjectPathHashLegacy.Length > 0 && other.ProjectPathHashLegacy.Length > 0)
                return string.Equals(ProjectPathHashLegacy, other.ProjectPathHashLegacy, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        /// <summary>
        /// True when a connection serving <paramref name="project"/> may serve a session pinned to <paramref name="pin"/>:
        /// always without a pin, else when the project matches it, or when the connection named no project (a plugin
        /// before the handshake metadata: nothing shows it serves another project, so it is routed as before).
        /// </summary>
        public static bool Serves(PluginProject? project, string? pin)
            => string.IsNullOrEmpty(pin) || (project?.MatchesPin(pin) ?? true);

        /// <summary>The project a hub connection's query names; null when it names none (a plugin before the handshake metadata).</summary>
        public static PluginProject? FromQuery(IQueryCollection? query)
        {
            var hash = query?[Consts.MCP.Server.HubQuery.ProjectPathHash].ToString() ?? string.Empty;
            var legacy = query?[Consts.MCP.Server.HubQuery.ProjectPathHashLegacy].ToString() ?? string.Empty;
            return hash.Length == 0 && legacy.Length == 0 ? null : new PluginProject(hash, legacy);
        }
    }
}
