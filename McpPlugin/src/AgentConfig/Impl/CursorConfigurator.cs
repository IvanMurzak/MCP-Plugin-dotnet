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
    /// <summary>Configurator for the Cursor AI agent.</summary>
    public sealed class CursorConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Cursor";
        public override string AgentId => "cursor";
        public override string DownloadUrl => "https://cursor.com/download";
        public override string TutorialUrl => "https://www.youtube.com/watch?v=dyk-4gTolSU";
        public override string? SkillsPath => ".cursor/skills";
        public override string? IconName => "cursor-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".cursor", "mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- '.cursor/mcp.json' file must have no json syntax errors.",
                "- Open Cursor settings window, go to 'MCP Servers' to restart ai-game-developer or to get more information about the available MCP tools and the status of the server.");
    }
}
