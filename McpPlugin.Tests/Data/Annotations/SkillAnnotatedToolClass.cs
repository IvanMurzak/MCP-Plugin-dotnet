/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Tests.Data.Annotations
{
    /// <summary>
    /// Tools carrying the optional skill metadata attributes, so a test can observe
    /// what <c>tools/list</c> reports for each combination of present / absent.
    /// </summary>
    [AiToolType]
    public static class SkillAnnotatedToolClass
    {
        public const string BothDescription = "Short blurb for the both-attributes tool";
        public const string BothBody = "# Both\n\nLong-form markdown body with a `code` sample.";
        public const string DescriptionOnlyDescription = "Short blurb, no body";
        public const string BodyOnlyBody = "# Body only\n\nNo short blurb on this one.";

        [AiTool("skill-tool-both", "Tool With Both Skill Attributes")]
        [AiSkillDescription(BothDescription)]
        [AiSkillBody(BothBody)]
        public static void Both() { }

        [AiTool("skill-tool-description-only", "Tool With Only A Skill Description")]
        [AiSkillDescription(DescriptionOnlyDescription)]
        public static void DescriptionOnly() { }

        [AiTool("skill-tool-body-only", "Tool With Only A Skill Body")]
        [AiSkillBody(BodyOnlyBody)]
        public static void BodyOnly() { }

        [AiTool("skill-tool-none", "Tool With No Skill Attributes")]
        public static void None() { }
    }
}
