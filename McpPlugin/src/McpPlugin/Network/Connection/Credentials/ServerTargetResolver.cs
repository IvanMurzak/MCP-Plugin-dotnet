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
using com.IvanMurzak.McpPlugin.AgentConfig;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Resolves which server a plugin connects to (hosted ai-game.dev vs a local hub URL) from the
    /// tool-neutral per-project marker (mcp-authorize b7, design 06). The project marker
    /// (<c>&lt;project&gt;/.ai-game-dev/project.json</c>) is the single source of the enrolled server target,
    /// so the plugin and any terminal-written config can never diverge on where the project points. Resolution
    /// priority: the project marker's <c>serverTarget</c>, then the credential's issued <c>serverTarget</c>,
    /// then the hosted default.
    /// </summary>
    public static class ServerTargetResolver
    {
        /// <summary>The hosted ai-game.dev server target (the default when nothing else is enrolled).</summary>
        public const string HostedTarget = "https://ai-game.dev";

        /// <summary>
        /// Resolve the enrolled server target URL for the project rooted at <paramref name="projectRootPath"/>.
        /// The project marker wins, then <paramref name="credentials"/>'s issued target, then
        /// <paramref name="fallback"/> (hosted by default).
        /// </summary>
        public static string Resolve(string? projectRootPath, MachineCredentials? credentials = null, string fallback = HostedTarget)
        {
            var markerTarget = ReadMarkerTarget(projectRootPath);
            if (!string.IsNullOrWhiteSpace(markerTarget))
                return markerTarget!;

            if (!string.IsNullOrWhiteSpace(credentials?.ServerTarget))
                return credentials!.ServerTarget!;

            return fallback;
        }

        /// <summary>
        /// True when <paramref name="target"/> is a hosted (non-loopback) server; false for a localhost hub
        /// URL or an unparseable/empty value.
        /// </summary>
        public static bool IsHosted(string? target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                return false;
            return !uri.IsLoopback;
        }

        static string? ReadMarkerTarget(string? projectRootPath)
        {
            if (string.IsNullOrWhiteSpace(projectRootPath))
                return null;
            try
            {
                return ProjectMarker.Read(projectRootPath!)?.ServerTarget;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
