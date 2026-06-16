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
    /// Configurator for the Cline AI agent (global per-OS config; http uses
    /// <c>type=streamableHttp</c>).
    /// </summary>
    public sealed class ClineConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Cline";
        public override string AgentId => "cline";
        public override string DownloadUrl => "https://cline.bot/";
        public override string? SkillsPath => ".cline/skills";
        public override string? IconName => "cline-64.png";

        private static string GlobalConfigPath(AgentConfiguratorSettings s)
        {
            const string relSettings = "globalStorage";
            switch (s.OperatingSystem)
            {
                case OperatingSystemKind.Windows:
                    // Build %APPDATA% deterministically from UserProfile so the path is driven solely by the
                    // injected OperatingSystemKind, not the host OS. SpecialFolder.ApplicationData maps to
                    // ~/.config on Linux, which would collide with the Linux branch when run on a Linux host.
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "Roaming", "Code", "User", relSettings, "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json");
                case OperatingSystemKind.MacOS:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Code", "User", relSettings, "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json");
                default: // Linux
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config", "Code", "User", relSettings, "saoudrizwan.claude-dev", "settings", "cline_mcp_settings.json");
            }
        }

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, GlobalConfigPath(settings), settings, logger);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, GlobalConfigPath(settings), bodyPath: DefaultBodyPath, logger: logger)
                .SetProperty("type", JsonValue.Create($"{TransportMethod.streamableHttp}")!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);
    }
}
