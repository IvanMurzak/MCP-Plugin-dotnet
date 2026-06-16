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
    /// <summary>Configurator for the Claude Code AI agent.</summary>
    public sealed class ClaudeCodeConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Claude Code";
        public override string AgentId => "claude-code";
        public override string DownloadUrl => "https://docs.anthropic.com/en/docs/claude-code/overview";
        public override string TutorialUrl => "https://youtu.be/Sknh2p12W8c";
        public override string? SkillsPath => ".claude/skills";
        public override string? IconName => "claude-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcpServers", logger: logger)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("type")
                .SetPropertyToRemove("url");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, bodyPath: "mcpServers");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
        {
            var isAuthRequired = settings.IsHttpAuthRequired;
            var token = !string.IsNullOrEmpty(settings.Token) ? settings.Token! : "<token>";

            if (transport == TransportMethod.stdio)
            {
                var authArgs = settings.IsStdioAuthRequired
                    ? $" {Args.Authorization}={AuthOption.required} {Args.Token}={token}"
                    : string.Empty;
                var addCommand = $"claude mcp add {AiAgentConfig.DefaultMcpServerName} \"{settings.ExecutableFullPath}\" port={settings.Port} plugin-timeout={settings.TimeoutMs} client-transport=stdio{authArgs}";
                return new[]
                {
                    new ConfigurationSection("Start", true, new[]
                    {
                        ConfigurationItem.Description("Navigate to project root"),
                        ConfigurationItem.ReadOnlyField($"cd \"{settings.ProjectRootPath}\""),
                        ConfigurationItem.Description("Launch Claude Code"),
                        ConfigurationItem.ReadOnlyField("claude")
                    }),
                    new ConfigurationSection("Manual Configuration Steps", false, new[]
                    {
                        ConfigurationItem.Description("Run the following command in the project folder to configure Claude Code"),
                        ConfigurationItem.ReadOnlyField(addCommand),
                        ConfigurationItem.Description("Restart or start Claude Code to apply the configuration"),
                        ConfigurationItem.ReadOnlyField("claude")
                    })
                };
            }

            var authHeader = isAuthRequired ? $" --header \"Authorization: Bearer {token}\"" : string.Empty;
            var addCommandHttp = $"claude mcp add --transport http {AiAgentConfig.DefaultMcpServerName} {settings.Host}{authHeader}";
            return new[]
            {
                new ConfigurationSection("Start", true, new[]
                {
                    ConfigurationItem.Description("Navigate to project root"),
                    ConfigurationItem.ReadOnlyField($"cd \"{settings.ProjectRootPath}\""),
                    ConfigurationItem.Description("Launch Claude Code"),
                    ConfigurationItem.ReadOnlyField("claude")
                }),
                new ConfigurationSection("Manual Configuration Steps", false, new[]
                {
                    ConfigurationItem.Description("Run the following command in the project folder to configure Claude Code"),
                    ConfigurationItem.ReadOnlyField(addCommandHttp),
                    ConfigurationItem.Description("Restart or start Claude Code to apply the configuration"),
                    ConfigurationItem.ReadOnlyField("claude")
                })
            };
        }
    }
}
