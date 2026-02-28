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
    /// Specifies where additional custom content returned by
    /// <see cref="SkillFileGenerator.GetAdditionalContent"/> is injected into a generated SKILL.md file.
    /// </summary>
    public enum SkillAdditionalContentPosition
    {
        /// <summary>
        /// No additional content is injected, regardless of what
        /// <see cref="SkillFileGenerator.GetAdditionalContent"/> returns.
        /// </summary>
        None = 0,

        /// <summary>
        /// Inject the additional content immediately after the title and description block,
        /// before the "How to Call" section.
        /// </summary>
        AfterTitle = 1,

        /// <summary>
        /// Inject the additional content after the "How to Call" section,
        /// before the "Input" section.
        /// </summary>
        AfterHowToCall = 2,

        /// <summary>
        /// Inject the additional content after the "Input" section,
        /// before the "Output" section.
        /// </summary>
        AfterInput = 3,

        /// <summary>
        /// Inject the additional content at the very end of the file, after all other sections.
        /// This is the default position.
        /// </summary>
        End = 4
    }
}
