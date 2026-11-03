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

        /// <summary>Where a Claude user adds a custom connector (claude.ai and Claude Desktop share the account setting).</summary>
        public const string ConnectorsSettingsUrl = "https://claude.ai/customize/connectors";

        // Claude Desktop only spawns stdio servers from claude_desktop_config.json and skips an HTTP entry there
        // as "not valid MCP server configurations"; remote servers go through its Connectors UI instead.
        public override bool SupportsTransport(TransportMethod transport) => transport == TransportMethod.stdio;

        // Claude reaches a custom connector from Anthropic's cloud, never from this machine: the public Cloud URL
        // works, a local (loopback / LAN) server does not. Nothing is written, so the user adds it by hand once.
        protected override IReadOnlyList<ConfigurationSection>? BuildManualSetupSections(
            AgentConfiguratorSettings settings, TransportMethod transport)
        {
            if (settings.ConnectionMode != ConnectionMode.Cloud)
                return null;

            return new[]
            {
                new ConfigurationSection("Add as a Claude Connector", true, new[]
                {
                    ConfigurationItem.Description("Claude Desktop and Claude in the browser connect to the Cloud server through a custom connector. Add it once in Claude; nothing is written on this computer."),
                    ConfigurationItem.Description("1. Open Customize > Connectors in Claude:"),
                    ConfigurationItem.Link("Open Claude Connectors", ConnectorsSettingsUrl),
                    ConfigurationItem.Description("2. Click + Add, then Add custom connector."),
                    ConfigurationItem.Description($"3. Name it {AiAgentConfig.DefaultMcpServerName} and paste this server URL:"),
                    ConfigurationItem.ReadOnlyField(settings.PinnedHttpUrl),
                    ConfigurationItem.Description("4. Click Continue, keep the detected sign-in settings, click Add, then sign in with your AI Game Developer account."),
                    ConfigurationItem.Description("5. In a chat, make sure the connector is turned on in the connectors menu.")
                }),
                new ConfigurationSection("Notes", false, new[]
                {
                    ConfigurationItem.Description("- The URL is unique to this project: add one connector per project."),
                    ConfigurationItem.Description("- The Free Claude plan allows one custom connector."),
                    ConfigurationItem.Description("- On Team and Enterprise plans an organization Owner adds it in Organization settings > Connectors; members then click Connect."),
                    ConfigurationItem.Description("- For a local server without the Cloud, select the Custom connection and the stdio transport instead.")
                })
            };
        }

        protected override string UnsupportedTransportMessage(AgentConfiguratorSettings settings) =>
            "Claude Desktop can't use a local server over HTTP: it skips an HTTP entry in claude_desktop_config.json, and " +
            "Claude connectors reach servers from Anthropic's cloud, not from this computer. Select the stdio transport and press Configure, " +
            "or switch the connection to Cloud and add it as a Claude connector.";

        protected override string? RestartNoticeText =>
            "Fully quit Claude Desktop (also from the system tray or menu bar) and start it again. " +
            "It reads claude_desktop_config.json only at startup.";

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, ConfigPath(settings), settings, logger);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, ConfigPath(settings), settings, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        // Stdio only: Describe() never builds sections for the unsupported HTTP transport.
        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- Claude Desktop may launch two MCP server instances instead of one. If you must use Claude Desktop, manually terminate one of the instances. This behavior is unreliable — consider switching to Claude Code.",
                "- Claude Desktop may not detect runtime updates to MCP tools. Ensure Claude Desktop reads the MCP tools on startup.",
                "- Start the plugin first; the connection status should read 'Connecting...'");
    }
}
