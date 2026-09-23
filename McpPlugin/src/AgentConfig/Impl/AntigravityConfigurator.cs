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
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Impl
{
    /// <summary>
    /// Configurator for the Antigravity AI agent (global config, <c>serverUrl</c> for http,
    /// <c>disabled</c> flag). Antigravity reads its global MCP config from ONE of two locations, and which
    /// one is not predictable per machine/install, so the entry is written to BOTH
    /// (<c>~/.gemini/config/mcp_config.json</c> and <c>~/.gemini/antigravity/mcp_config.json</c>) through a <see cref="CompositeAiAgentConfig"/>.
    /// </summary>
    public sealed class AntigravityConfigurator : AiAgentConfigurator
    {
        private readonly string? _userProfileOverride;

        public AntigravityConfigurator() { }

        /// <summary>Test seam: resolve the candidate paths under <paramref name="userProfile"/> instead of the real profile.</summary>
        internal AntigravityConfigurator(string userProfile) => _userProfileOverride = userProfile;

        public override string AgentName => "Antigravity";
        public override string AgentId => "antigravity";
        public override string DownloadUrl => "https://antigravity.google/download";
        public override string? SkillsPath => ".agent/skills";
        public override string? IconName => "antigravity-64.png";

        /// <summary>
        /// The candidate global config files, in priority order:
        /// <c>&lt;UserProfile&gt;/.gemini/config/mcp_config.json</c> and
        /// <c>&lt;UserProfile&gt;/.gemini/antigravity/mcp_config.json</c>.
        /// </summary>
        private IReadOnlyList<string> GlobalConfigPaths()
        {
            var home = _userProfileOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Path.Combine(home, ".gemini", "config", "mcp_config.json"),
                Path.Combine(home, ".gemini", "antigravity", "mcp_config.json")
            };
        }

        protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => Composite(logger, path => Entry(path, logger)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", AgentConfigBuilders.StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("serverUrl"));

        protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, ILogger? logger)
            => Composite(logger, path => Entry(path, logger)
                .SetProperty("serverUrl", JsonValue.Create(settings.PinnedHttpUrl)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args"));

        /// <summary>The transport-independent part of the entry: <c>disabled:false</c>, no <c>url</c>/<c>type</c>.</summary>
        private JsonAiAgentConfig Entry(string path, ILogger? logger)
            => new JsonAiAgentConfig(AgentName, path, bodyPath: "mcpServers", logger: logger)
                .AddIdentityKey("serverUrl")
                .SetProperty("disabled", JsonValue.Create(false)!, requiredForConfiguration: true)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("type");

        private CompositeAiAgentConfig Composite(ILogger? logger, Func<string, JsonAiAgentConfig> build)
            => new CompositeAiAgentConfig(AgentName, GlobalConfigPaths().Select(build).ToArray(), logger);

        protected override IReadOnlyList<ConfigurationSection> BuildSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => DefaultConfigurationSections(settings, transport, logger);

        protected override IReadOnlyList<ConfigurationSection> BuildTroubleshootingSections(
            AgentConfiguratorSettings settings, TransportMethod transport, ILogger? logger)
            => TroubleshootingSection(
                "- Ensure MCP configuration file doesn't have syntax errors",
                "- Restart Antigravity after configuration changes");
    }
}
