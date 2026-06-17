/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>Configurator for the Visual Studio Code (Copilot) AI agent.</summary>
    public sealed class VisualStudioCodeCopilotConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Visual Studio Code (Copilot)";
        public override string AgentId => "vscode-copilot";
        public override string DownloadUrl => "https://code.visualstudio.com/download";
        public override string TutorialUrl => "https://www.youtube.com/watch?v=ZhP7Ju91mOE";
        public override string? SkillsPath => ".claude/skills";
        public override string? IconName => "vs-code-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".vscode", "mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger, bodyPath: "servers");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, bodyPath: "servers");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- '.vscode/mcp.json' file must have no json syntax errors.",
                "- Restart Visual Studio Code after configuration changes");
    }
}
