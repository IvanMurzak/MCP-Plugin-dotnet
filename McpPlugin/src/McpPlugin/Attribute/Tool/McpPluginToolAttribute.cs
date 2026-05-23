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

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Deprecated alias for <see cref="AiToolAttribute"/>. Kept as an
    /// <see cref="ObsoleteAttribute"/>-marked subclass so existing decorations on consumer methods
    /// continue to be discovered by reflection lookups for <see cref="AiToolAttribute"/>.
    /// </summary>
    [Obsolete("Use [AiTool] instead. This alias will be removed in a future major release.")]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class McpPluginToolAttribute : AiToolAttribute
    {
        public McpPluginToolAttribute(string name, string? title = null)
            : base(name, title)
        {
        }
    }
}
