/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Generic;
using System.Linq;

namespace com.IvanMurzak.McpPlugin.Common
{
    /// <summary>
    /// A game engine the AI Game Developer stack supports: its wire id plus how its plugin is installed.
    /// </summary>
    public sealed class KnownEngine
    {
        public KnownEngine(string id, string? cliPackage)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            CliPackage = cliPackage;
        }

        /// <summary>Lower-case wire id, e.g. <c>"unity"</c>.</summary>
        public string Id { get; }

        /// <summary>
        /// The public npm CLI package that installs this engine's plugin (<c>npx &lt;package&gt; install-plugin</c>),
        /// or <c>null</c> when the engine has no public CLI and its plugin is installed via the AI Game Developer App.
        /// </summary>
        public string? CliPackage { get; }
    }

    /// <summary>
    /// The single source of truth for the engine ids accepted on the wire (project-key mint, enrollment,
    /// instance metadata). Shared by the client (<c>McpPlugin</c>) and the server (<c>McpPlugin.Server</c>).
    /// </summary>
    public static class KnownEngineIds
    {
        public const string Unity = "unity";
        public const string Godot = "godot";
        public const string Unreal = "unreal";
        public const string Brutal = "brutal";

        /// <summary>Sentinel sent for an engine that is not in <see cref="All"/>; never a real engine.</summary>
        public const string Unknown = "unknown";

        /// <summary>Every supported engine, in display order.</summary>
        public static readonly IReadOnlyList<KnownEngine> All = new[]
        {
            new KnownEngine(Unity, "unity-mcp-cli"),
            new KnownEngine(Godot, "godot-cli"),
            new KnownEngine(Unreal, "unreal-mcp-cli"),
            new KnownEngine(Brutal, cliPackage: null), // installer is private, inside the AI Game Developer App
        };

        /// <summary>The ids of <see cref="All"/>, in the same order (excludes <see cref="Unknown"/>).</summary>
        public static readonly IReadOnlyList<string> Ids = All.Select(e => e.Id).ToArray();

        /// <summary>The engine with this exact (already normalised) id, or <c>null</c>.</summary>
        public static KnownEngine? Find(string? id)
            => id == null ? null : All.FirstOrDefault(e => e.Id == id);

        /// <summary>True when <paramref name="id"/> is exactly one of <see cref="Ids"/>.</summary>
        public static bool IsKnown(string? id) => Find(id) != null;

        /// <summary>Trims and lower-cases <paramref name="engine"/>; returns it when known, else <see cref="Unknown"/>.</summary>
        public static string Normalize(string? engine)
        {
            var e = (engine ?? string.Empty).Trim().ToLowerInvariant();
            return IsKnown(e) ? e : Unknown;
        }
    }
}
