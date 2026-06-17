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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>
    /// Configurator for the Antigravity AI agent (global config, <c>serverUrl</c> for http,
    /// <c>disabled</c> flag).
    /// </summary>
    public sealed class AntigravityConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Antigravity";
        public override string AgentId => "antigravity";
        public override string DownloadUrl => "https://antigravity.google/download";
        public override string? SkillsPath => ".agent/skills";
        public override string? IconName => "antigravity-64.png";

        private static string GlobalConfigPath(AgentConfiguratorSettings s) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "config", "mcp_config.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), bodyPath: "mcpServers", logger: logger)
                .AddIdentityKey("serverUrl")
                .SetProperty("disabled", JsonValue.Create(false)!, requiredForConfiguration: true)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("serverUrl")
                .SetPropertyToRemove("type");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), bodyPath: "mcpServers", logger: logger)
                .AddIdentityKey("serverUrl")
                .SetProperty("disabled", JsonValue.Create(false)!, requiredForConfiguration: true)
                .SetProperty("serverUrl", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args")
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("type");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- Ensure MCP configuration file doesn't have syntax errors",
                "- Restart Antigravity after configuration changes");
    }
}
