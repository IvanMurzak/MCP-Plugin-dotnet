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
        string Content { get; }
        bool Enabled { get; }
    }
}
