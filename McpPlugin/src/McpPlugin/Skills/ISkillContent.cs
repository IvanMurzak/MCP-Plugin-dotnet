/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Skills
{
    /// <summary>
    /// Represents a custom skill with a name, description, and markdown content body.
    /// </summary>
    public interface ISkillContent
    {
        string Name { get; }
        string? Description { get; }

        /// <summary>
        /// Optional concise description used for the SKILL.md YAML <c>description:</c> field when
        /// <see cref="Description"/> would overflow the 1024-character cap. When <see langword="null"/>,
        /// <see cref="SkillFileGenerator"/> falls back to <see cref="Description"/> (truncated to fit).
        /// </summary>
        string? SkillDescription { get; }

        string Content { get; }
        bool Enabled { get; }
    }
}
