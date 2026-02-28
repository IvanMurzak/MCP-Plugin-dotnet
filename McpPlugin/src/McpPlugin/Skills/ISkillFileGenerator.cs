/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Skills
{
    /// <summary>
    /// Defines the contract for generating and deleting AI skill markdown files
    /// for registered MCP tools. Implement this interface to customize the skill
    /// file generation format used by <see cref="McpPlugin"/>.
    /// </summary>
    public interface ISkillFileGenerator
    {
        /// <summary>
        /// Generates skill markdown files for all provided tools under <paramref name="skillsPath"/>.
        /// <paramref name="skillsPath"/> may be an absolute or relative path; relative paths are resolved
        /// against the current working directory at the time of the call.
        /// Provide <paramref name="host"/> to include correct API endpoint URLs in the generated markdown.
        /// </summary>
        bool Generate(IEnumerable<IRunTool> tools, string skillsPath, string host);

        /// <summary>
        /// Deletes the skill subdirectory for each tool in <paramref name="tools"/> from
        /// <paramref name="skillsPath"/>. Only the subdirectories that correspond to the provided
        /// tools are removed; all other content inside <paramref name="skillsPath"/> is left intact.
        /// </summary>
        bool Delete(IEnumerable<IRunTool> tools, string skillsPath);
    }
}
