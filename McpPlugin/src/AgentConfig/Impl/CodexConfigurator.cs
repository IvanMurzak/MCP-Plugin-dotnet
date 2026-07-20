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
    /// <summary>
    /// Configurator for the Codex AI agent — the only TOML-based agent. HTTP auth is injected
    /// via a <c>bearer_token_env_var</c> indirection rather than an inline header.
    /// </summary>
    public sealed class CodexConfigurator : AiAgentConfigurator
    {
        private const string EnvVarNameAuthToken = "GAME_DEV_AUTH_TOKEN";
        private const string EnvVarNameBearerToken = "bearer_token_env_var";

        public override string AgentName => "Codex";
        public override string AgentId => "codex";
        public override string DownloadUrl => "https://openai.com/codex/";
        public override string? SkillsPath => ".agents/skills";
        public override string? IconName => "codex-64.png";

        private static string LocalConfigPath(AgentConfiguratorSettings s) => Path.Combine(s.ProjectRootPath, ".codex", "config.toml");

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new TomlAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcp_servers", logger: logger)
                .SetProperty("enabled", true, requiredForConfiguration: true)
                .SetProperty("command", settings.ExecutableFullPath.Replace('\\', '/'), requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                // Codex is TOML-only, so it hand-rolls the arg list AgentConfigBuilders.StdioArgs builds
                // for the JSON agents. Keep it in lockstep — notably the PinnedPort precedence
                // (marker portOverride > port typed into Host > derived v2), auth-fixes T1 / defect A.
                .SetProperty("args", new[]
                {
                    $"{Args.Port}={settings.PinnedPort}",
                    $"{Args.PluginTimeout}={settings.TimeoutMs}",
                    $"{Args.ClientTransportMethod}={TransportMethod.stdio}",
                    $"{Args.Project}={settings.ProjectPin}"
                }, requiredForConfiguration: true)
                .SetProperty("tool_timeout_sec", 300, requiredForConfiguration: false)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("type")
                .SetPropertyToRemove("startup_timeout_sec");

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => new TomlAiAgentConfig(AgentName, LocalConfigPath(settings), bodyPath: "mcp_servers", logger: logger)
                .SetProperty("enabled", true, requiredForConfiguration: true)
                .SetProperty("url", settings.PinnedHttpUrl, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetProperty("tool_timeout_sec", 300, requiredForConfiguration: false)
                .SetProperty("startup_timeout_sec", 30, requiredForConfiguration: false)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args")
                .SetPropertyToRemove("type");

        protected override void ApplyHttpAuthorization(AiAgentConfig config, AgentConfiguratorSettings settings, HttpCredentialMode credentialMode)
        {
            base.ApplyHttpAuthorization(config, settings, credentialMode);

            var tomlConfig = config as TomlAiAgentConfig
                ?? throw new InvalidCastException($"Expected TomlAiAgentConfig for Codex HTTP configuration but got {config.GetType().Name}");

            // Advanced PAT path only (mcp-authorize b6): Codex reads the token from the
            // GAME_DEV_AUTH_TOKEN env var, so the secret never lands in the config file (the
            // preferred placement). The default OAuth path writes neither the indirection nor a token.
            if (credentialMode == HttpCredentialMode.AccessToken && !string.IsNullOrEmpty(settings.Token))
                tomlConfig.SetProperty(EnvVarNameBearerToken, EnvVarNameAuthToken, requiredForConfiguration: true);
            else
                tomlConfig.SetPropertyToRemove(EnvVarNameBearerToken);
        }

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
        {
            var addStdio = $"codex mcp add {AiAgentConfig.DefaultMcpServerName} \"{settings.ExecutableFullPath}\" port={settings.Port} plugin-timeout={settings.TimeoutMs} client-transport=stdio";
            var addHttp = $"codex mcp add {AiAgentConfig.DefaultMcpServerName} --url {settings.Host}";
            if (settings.IsHttpAuthRequired)
            {
                addStdio += $" --bearer-token-env-var={EnvVarNameAuthToken}";
                addHttp += $" --bearer-token-env-var={EnvVarNameAuthToken}";
            }

            if (transport == TransportMethod.stdio)
            {
                return new[]
                {
                    new ConfigurationSection("Manual Configuration Steps - Option 1", true, new[]
                    {
                        ConfigurationItem.Description("1. Open a terminal and run the following command to be in the project folder"),
                        ConfigurationItem.ReadOnlyField($"cd \"{settings.ProjectRootPath}\""),
                        ConfigurationItem.Description("2. Run the following command in the project folder to configure Codex"),
                        ConfigurationItem.ReadOnlyField(addStdio),
                        ConfigurationItem.Description("3. Start Codex"),
                        ConfigurationItem.ReadOnlyField("codex")
                    }),
                    new ConfigurationSection("Manual Configuration Steps - Option 2", false, new[]
                    {
                        ConfigurationItem.Description("1. Open or create file '.codex/config.toml'"),
                        ConfigurationItem.Description("2. Copy and paste the configuration TOML into the file."),
                        ConfigurationItem.ReadOnlyField(GetStdioConfig(settings, logger).ExpectedFileContent)
                    })
                };
            }

            var items = new List<ConfigurationItem>();
            if (settings.IsHttpAuthRequired)
            {
                items.Add(ConfigurationItem.Warning($"Authorization is enabled. Set the '{EnvVarNameAuthToken}' environment variable before starting Codex in terminal."));
                items.Add(settings.IsWindows
                    ? ConfigurationItem.ReadOnlyField($"setx {EnvVarNameAuthToken} \"{settings.Token}\"")
                    : ConfigurationItem.ReadOnlyField($"export {EnvVarNameAuthToken}=\"{settings.Token}\""));
            }
            items.Add(ConfigurationItem.Description("1. Open a terminal and run the following command to be in the project folder"));
            items.Add(ConfigurationItem.ReadOnlyField($"cd \"{settings.ProjectRootPath}\""));
            items.Add(ConfigurationItem.Description("2. Run the following command in the project folder to configure Codex"));
            items.Add(ConfigurationItem.ReadOnlyField(addHttp));
            items.Add(ConfigurationItem.Description("3. Start Codex"));
            items.Add(ConfigurationItem.ReadOnlyField("codex"));

            return new[]
            {
                new ConfigurationSection("Manual Configuration Steps - Option 1", true, items),
                new ConfigurationSection("Manual Configuration Steps - Option 2", false, new[]
                {
                    ConfigurationItem.Description("1. Open or create file '.codex/config.toml'"),
                    ConfigurationItem.Description("2. Copy and paste the configuration TOML into the file."),
                    ConfigurationItem.ReadOnlyField(GetHttpConfig(settings, logger).ExpectedFileContent)
                })
            };
        }

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- Ensure Codex CLI is installed and accessible from terminal",
                "- Restart Codex after configuration changes");
    }
}
