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
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>Configurator for the GitHub Copilot CLI AI agent.</summary>
    public sealed class GitHubCopilotCliConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "GitHub Copilot CLI";
        public override string AgentId => "github-copilot-cli";
        public override string DownloadUrl => "https://github.com/features/copilot/cli";
        public override string? SkillsPath => ".claude/skills";
        public override string? IconName => "github-copilot-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcpServers", logger: logger)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
                .SetProperty("tools", new JsonArray { "*" }, requiredForConfiguration: false)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("type");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, bodyPath: "mcpServers")
                .SetProperty("tools", new JsonArray { "*" }, requiredForConfiguration: false);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- Ensure Copilot CLI is launched from the project root (the folder containing '.mcp.json')",
                "- Requires GitHub Copilot CLI v1.0.12+ which discovers '.mcp.json' at project level",
                "- Ensure MCP configuration file doesn't have syntax errors",
                "- Restart Copilot CLI after configuration changes");
    }
}
