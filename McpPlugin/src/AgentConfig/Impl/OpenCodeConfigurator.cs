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
    /// Configurator for the Open Code AI agent. Open Code uses a single <c>command</c>
    /// array (executable + inline args) rather than separate <c>command</c> + <c>args</c>,
    /// and <c>type</c> = <c>local</c>/<c>remote</c>.
    /// </summary>
    public sealed class OpenCodeConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Open Code";
        public override string AgentId => "open-code";
        public override string DownloadUrl => "https://opencode.ai/download";
        public override string? SkillsPath => ".opencode/skills";
        public override string? IconName => "open-code-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, "opencode.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
        {
            var commandArray = new JsonArray
            {
                settings.ExecutableFullPath.Replace('\\', '/'),
                $"{Args.Port}={settings.Port}",
                $"{Args.PluginTimeout}={settings.TimeoutMs}",
                $"{Args.ClientTransportMethod}={TransportMethod.stdio}",
                $"{Args.Authorization}={settings.AuthOption}"
            };
            if (settings.IsStdioAuthRequired && !string.IsNullOrEmpty(settings.Token))
                commandArray.Add($"{Args.Token}={settings.Token}");

            return new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcp", logger: logger)
                .SetProperty("type", JsonValue.Create("local")!, requiredForConfiguration: true)
                .SetProperty("enabled", JsonValue.Create(true)!, requiredForConfiguration: true)
                .SetProperty("command", commandArray, requiredForConfiguration: true)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("args");
        }

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcp", logger: logger)
                .SetProperty("type", JsonValue.Create("remote")!, requiredForConfiguration: true)
                .SetProperty("enabled", JsonValue.Create(true)!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");

        // OpenCode embeds the token in the command array (not the args), so the base
        // args-based STDIO authorization injection does not apply.
        protected override void ApplyStdioAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings)
        {
            // no-op: token is handled inline in CreateStdioConfig's command array.
        }

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);
    }
}
