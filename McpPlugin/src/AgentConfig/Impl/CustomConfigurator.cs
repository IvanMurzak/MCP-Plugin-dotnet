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
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>
    /// Configurator for a custom / unlisted MCP client. It has no auto-detectable config file;
    /// the user copies the generated snippet manually, and the skills path is user-editable
    /// (exposed via an <see cref="ConfigurationItemKind.EditableField"/>).
    /// </summary>
    public sealed class CustomConfigurator : AiAgentConfigurator
    {
        /// <summary>
        /// The user-editable skills path. Defaults to the standard skills folder; engines
        /// set this from their persisted value and persist edits made through the editable field.
        /// </summary>
        public string EditableSkillsPath { get; set; } = ".claude/skills";

        public override string AgentName => "Other - Custom";
        public override string AgentId => "other-custom";
        public override string DownloadUrl => "NA";
        public override string? SkillsPath => EditableSkillsPath;
        public override string? IconName => null;

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => throw new NotImplementedException("CustomConfigurator has no auto-detectable config; use the generated snippet from the description instead.");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => throw new NotImplementedException("CustomConfigurator has no auto-detectable config; use the generated snippet from the description instead.");

        // The custom configurator cannot detect an MCP config on disk.
        public override bool IsConfigured(AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger = null) => false;

        // No detectable config => always NotConfigured (never stale). Avoids the throwing
        // CreateStdioConfig/CreateHttpConfig that the base GetStatus would otherwise invoke.
        public override ConfiguratorStatus GetStatus(AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger = null)
            => ConfiguratorStatus.NotConfigured;

        // Mirrors Unity's CustomConfigurator.DisableLinksContainer() — the custom agent emits no
        // download/tutorial links (its DownloadUrl "NA" is a placeholder, not a real link target).
        public override IReadOnlyList<ConfigurationItem> BuildLinks() => System.Array.Empty<ConfigurationItem>();

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
        {
            var items = new List<ConfigurationItem>
            {
                ConfigurationItem.Description("Skills output path (editable):"),
                ConfigurationItem.EditableField(EditableSkillsPath)
            };

            if (transport == TransportMethod.stdio)
            {
                var snippet = Consts.MCP.Server.Config(
                    executablePath: settings.ExecutableFullPath.Replace('\\', '/'),
                    serverName: AiAgentConfig.DefaultMcpServerName,
                    bodyPath: "mcpServers",
                    port: settings.Port,
                    timeoutMs: settings.TimeoutMs).ToString();
                items.Add(ConfigurationItem.Description("Copy paste the json into your MCP Client to configure it."));
                items.Add(ConfigurationItem.ReadOnlyField(snippet));
            }
            else
            {
                items.Add(ConfigurationItem.Description("Copy paste the json into your MCP Client to configure it."));
                items.Add(ConfigurationItem.ReadOnlyField(
                    $"{{\"mcpServers\":{{\"{AiAgentConfig.DefaultMcpServerName}\":{{\"type\":\"http\",\"url\":\"{settings.Host}\"}}}}}}"));
            }

            return new[] { new ConfigurationSection("Configuration", true, items) };
        }
    }
}
