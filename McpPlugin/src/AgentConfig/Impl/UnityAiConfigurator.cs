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
    /// <summary>Configurator for the Unity AI agent.</summary>
    public sealed class UnityAiConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Unity AI";
        public override string AgentId => "unity-ai";
        public override string DownloadUrl => "https://unity.com/features/ai";
        public override string? IconName => "unity-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, "UserSettings", "mcp.json");

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
                "- 'UserSettings/mcp.json' file must have no json syntax errors.",
                "- Open Unity AI settings window\n- Go to Edit > Project Settings > AI > MCP Servers\n- Click 'Restart ai-game-developer' button or check the status of the server.");
    }
}
