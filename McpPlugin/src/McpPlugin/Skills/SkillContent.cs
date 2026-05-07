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
    /// Immutable implementation of <see cref="ISkillContent"/>.
    /// </summary>
    public class SkillContent : ISkillContent
    {
        public string Name { get; }
        public string? Description { get; }
        public string? SkillDescription { get; }
        public string Content { get; }
        public bool Enabled { get; }

        public SkillContent(string name, string? description, string content, bool enabled = true, string? skillDescription = null)
        {
            Name = name;
            Description = description;
            SkillDescription = skillDescription;
            Content = content;
            Enabled = enabled;
        }
    }
}
