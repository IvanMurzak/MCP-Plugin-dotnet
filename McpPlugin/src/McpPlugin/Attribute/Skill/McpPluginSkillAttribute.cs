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
    /// Deprecated alias for <see cref="AiSkillAttribute"/>. Kept as an
    /// <see cref="ObsoleteAttribute"/>-marked subclass so existing decorations on consumer fields/properties
    /// continue to be discovered by reflection lookups for <see cref="AiSkillAttribute"/>.
    /// </summary>
    [Obsolete("Use [AiSkill] instead. This alias will be removed in a future major release.")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class McpPluginSkillAttribute : AiSkillAttribute
    {
        public McpPluginSkillAttribute(string name, string? description = null)
            : base(name, description)
        {
        }
    }
}
