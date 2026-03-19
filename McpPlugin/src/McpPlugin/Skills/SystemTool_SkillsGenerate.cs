/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.ComponentModel;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.Extensions.Options;

namespace com.IvanMurzak.McpPlugin.Skills
{
    [McpPluginToolType]
    public static class SystemTool_SkillsGenerate
    {
        [McpPluginTool("skills-generate", ToolType = McpToolType.System)]
        [Description("Generates skill markdown files for all registered MCP tools.")]
        public static ResponseCallTool Execute(
            IMcpPlugin plugin,
            IOptions<ConnectionConfig> connectionConfig,
            [Description("Absolute path to the skills output directory. If provided, overrides the configured skills path.")]
            string? skillsPath = null)
        {
            var config = connectionConfig.Value;

            if (!string.IsNullOrWhiteSpace(skillsPath))
                config.SkillsPath = skillsPath!;

            var success = plugin.GenerateSkillFiles();

            return success
                ? ResponseCallTool.Success("Skill files generated successfully.")
                : ResponseCallTool.Error("Failed to generate skill files.");
        }
    }
}
