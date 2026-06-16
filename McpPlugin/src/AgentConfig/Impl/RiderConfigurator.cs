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
    /// <summary>
    /// Configurator for the Rider (Junie) AI agent. Junie uses <c>enabled</c> (not
    /// <c>disabled</c>) and a project-local <c>.junie/mcp/mcp.json</c>.
    /// </summary>
    public sealed class RiderConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Rider (Junie)";
        public override string AgentId => "rider-junie";
        public override string DownloadUrl => "https://www.jetbrains.com/rider/download/";
        public override string? SkillsPath => ".junie/skills";
        public override string? IconName => "rider-64.png";

        private static string JunieConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".junie", "mcp", "mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, JunieConfigPath(settings), bodyPath: DefaultBodyPath, logger: logger)
                .SetProperty("enabled", JsonValue.Create(true)!, requiredForConfiguration: true)
                .SetPropertyToRemove("disabled")
                .SetProperty("type", JsonValue.Create("stdio")!, requiredForConfiguration: true)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("url");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, JunieConfigPath(settings), bodyPath: DefaultBodyPath, logger: logger)
                .SetProperty("enabled", JsonValue.Create(true)!, requiredForConfiguration: true)
                .SetPropertyToRemove("disabled")
                .SetProperty("type", JsonValue.Create("http")!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);
    }
}
