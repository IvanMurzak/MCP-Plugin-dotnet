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
                .SetProperty("url", JsonValue.Create(settings.PinnedHttpUrl)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
        {
            // HTTP transport: Rider/Junie connects via stdio only (HTTP is unreliable here), so
            // surface the warning and fall back to exposing the expected config content.
            if (transport != TransportMethod.stdio)
            {
                return new[]
                {
                    new ConfigurationSection("Configuration", true, new[]
                    {
                        ConfigurationItem.Warning("Rider (Junie) connects via stdio. Switch the transport method to 'stdio' to configure this agent."),
                        ConfigurationItem.ReadOnlyField(GetHttpConfig(settings, logger).ExpectedFileContent)
                    })
                };
            }

            // STDIO transport: restore Unity's per-OS manual-setup convenience command
            // (Win PowerShell New-Item/Set-Content vs Mac/Linux mkdir -p/printf), driven by
            // settings.OperatingSystem. Wording is engine-neutral ("the project folder").
            var relativePath = Path.Combine(".junie", "mcp", "mcp.json");
            var expectedContent = GetStdioConfig(settings, logger).ExpectedFileContent;

            string terminalDescription;
            string terminalCommand;
            if (settings.IsWindows)
            {
                terminalDescription = "Option 1: Run this command in PowerShell from the project folder";
                terminalCommand = $"New-Item -ItemType Directory -Force -Path .junie\\mcp | Out-Null; Set-Content -Path {relativePath.Replace('/', '\\')} -Value '{expectedContent.Replace("'", "''")}'";
            }
            else
            {
                terminalDescription = "Option 1: Run this command in a terminal from the project folder";
                terminalCommand = $"mkdir -p .junie/mcp && printf '%s\\n' '{expectedContent.Replace("'", "'\\''")}' > {relativePath.Replace('\\', '/')}";
            }

            return new[]
            {
                new ConfigurationSection("Manual Configuration Steps", true, new[]
                {
                    ConfigurationItem.Warning("After configuring, open Rider Settings / Tools / Junie / MCP Settings and enable the server to connect the AI agent."),
                    ConfigurationItem.Description(terminalDescription),
                    ConfigurationItem.ReadOnlyField(terminalCommand),
                    ConfigurationItem.Description($"Option 2: Create or open the file '{relativePath.Replace('\\', '/')}' and paste the JSON below."),
                    ConfigurationItem.ReadOnlyField(expectedContent),
                    ConfigurationItem.Description("Option 3: Open Rider Settings / Tools / Junie / MCP Settings and add a new server manually.")
                })
            };
        }

        // STDIO only: HTTP is disabled for Rider/Junie (the BuildSections HTTP branch surfaces a
        // warning instead), so Unity emits the Troubleshooting foldout for stdio only.
        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => transport == TransportMethod.stdio
                ? TroubleshootingSection(
                    "- Ensure MCP configuration file doesn't have syntax errors",
                    "- Restart Rider after configuration changes",
                    "- If using Terminal, ensure you are in your project root folder.")
                : System.Array.Empty<ConfigurationSection>();
    }
}
