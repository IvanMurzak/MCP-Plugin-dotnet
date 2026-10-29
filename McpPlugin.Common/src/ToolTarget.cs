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

namespace com.IvanMurzak.McpPlugin.Common
{
    /// <summary>
    /// Which plugin connections (<see cref="ClientRole"/>) register a tool. A plugin skips, at build time, every
    /// tool whose target does not include its own role, so a game-only tool declared in an assembly the editor
    /// also scans is not registered by the editor. The server then routes calls of a tool only a runtime has to
    /// that runtime, even while an editor is connected.
    /// </summary>
    [Flags]
    public enum ToolTarget
    {
        /// <summary>Registered by the engine editor only.</summary>
        Editor = 1,

        /// <summary>Registered by a running game (a player build, or a play-mode process) only.</summary>
        Runtime = 2,

        /// <summary>Registered by every connection (the default).</summary>
        Both = Editor | Runtime,
    }

    /// <summary>Helpers for <see cref="ToolTarget"/>.</summary>
    public static class ToolTargets
    {
        /// <summary>
        /// True when a connection of <paramref name="role"/> registers a tool of <paramref name="target"/>. An
        /// unset target (<c>0</c>) counts as <see cref="ToolTarget.Both"/>.
        /// </summary>
        public static bool Includes(this ToolTarget target, ClientRole role)
            => target == 0 || (target & (role == ClientRole.Runtime ? ToolTarget.Runtime : ToolTarget.Editor)) != 0;
    }
}
