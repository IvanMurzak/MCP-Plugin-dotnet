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
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AiToolTypeAttribute : Attribute
    {
        public string? Path { get; set; }

        /// <summary>
        /// Which plugin connections register the tools of this class: <see cref="ToolTarget.Both"/> (the default),
        /// <see cref="ToolTarget.Editor"/> or <see cref="ToolTarget.Runtime"/>. Mark game-only tools
        /// <see cref="ToolTarget.Runtime"/>: an editor that scans the same assembly then skips them, and the server
        /// routes their calls to the running game even while the editor is connected. A tool's own
        /// <see cref="AiToolAttribute.Target"/> overrides this. The plugin's role is
        /// <see cref="ConnectionConfig.ClientRole"/> (or a handshake environment ending in <c>(runtime)</c>).
        /// </summary>
        public ToolTarget Target { get; set; } = ToolTarget.Both;
    }
}
