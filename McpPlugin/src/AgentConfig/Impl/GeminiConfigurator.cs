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
    /// <summary>Configurator for the Gemini CLI AI agent.</summary>
    public sealed class GeminiConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Gemini";
        public override string AgentId => "gemini";
        public override string DownloadUrl => "https://geminicli.com/docs/get-started/installation/";
        public override string? SkillsPath => ".gemini/skills";
        public override string? IconName => "gemini-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".gemini", "settings.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        // STDIO carries the extra "--debug flag" hint (it helps the stdio transport work with
        // Gemini); HTTP omits it. Mirrors Unity's per-transport Troubleshooting content.
        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => transport == TransportMethod.stdio
                ? TroubleshootingSection(
                    "- Ensure Gemini CLI is installed and accessible from terminal",
                    "- Start Gemini with --debug flag, it helps MCP server to work properly with Gemini in stdio transport mode.",
                    "- Ensure MCP configuration file doesn't have syntax errors",
                    "- Restart Gemini after configuration changes")
                : TroubleshootingSection(
                    "- Ensure Gemini CLI is installed and accessible from terminal",
                    "- Ensure MCP configuration file doesn't have syntax errors",
                    "- Restart Gemini after configuration changes");
    }
}
