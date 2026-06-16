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
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>Configurator for the Claude Desktop AI agent (global, per-OS config path).</summary>
    public sealed class ClaudeDesktopConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Claude Desktop";
        public override string AgentId => "claude-desktop";
        public override string DownloadUrl => "https://code.claude.com/docs/en/desktop";
        public override string? IconName => "claude-64.png";

        // Windows %APPDATA% is built deterministically from UserProfile so the path is driven solely by the
        // injected OperatingSystemKind, not the host OS. SpecialFolder.ApplicationData maps to ~/.config on
        // Linux, which would make the "Windows" path host-dependent and indistinguishable from a Linux base.
        private static string ConfigPath(AgentConfiguratorSettings s) => s.IsWindows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "Claude", "claude_desktop_config.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude_desktop_config.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, ConfigPath(settings), settings, logger);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, ConfigPath(settings), settings, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);
    }
}
