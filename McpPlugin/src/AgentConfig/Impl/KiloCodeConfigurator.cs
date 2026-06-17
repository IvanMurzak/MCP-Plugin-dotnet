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
    /// <summary>Configurator for the Kilo Code AI agent.</summary>
    public sealed class KiloCodeConfigurator : AiAgentConfigurator
    {
        public override string AgentName => "Kilo Code";
        public override string AgentId => "kilo-code";
        public override string DownloadUrl => "https://app.kilo.ai/get-started";
        public override string? SkillsPath => ".kilocode/skills";
        public override string? IconName => "kilo-code-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".kilocode", "mcp.json");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonStdio(AgentName, LocalConfigPath(settings), settings, logger, disabled: false);

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => AgentConfigBuilders.JsonHttp(AgentName, LocalConfigPath(settings), settings, logger, type: "streamable-http", disabled: false);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        // STDIO carries the extra "executable path" line (relevant only when launching the
        // server binary); HTTP omits it. Mirrors Unity's per-transport Troubleshooting content.
        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => transport == TransportMethod.stdio
                ? TroubleshootingSection(
                    "- Ensure the JSON file has no syntax errors.",
                    "- Check that the executable path is correct.",
                    "- Verify Kilo Code has MCP support enabled.",
                    "- The configuration file should be in your project root, next to Assets folder.",
                    "- Restart Kilo Code after configuration changes")
                : TroubleshootingSection(
                    "- Ensure the JSON file has no syntax errors.",
                    "- Verify Kilo Code has MCP support enabled.",
                    "- The configuration file should be in your project root, next to Assets folder.",
                    "- Restart Kilo Code after configuration changes");
    }
}
