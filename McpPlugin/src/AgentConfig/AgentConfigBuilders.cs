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
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// Helpers that build the recurring stdio / http server-entry shapes shared by most
    /// JSON-based configurators. Centralises the <c>command</c> + <c>args</c> (stdio) and
    /// <c>type=http</c> + <c>url</c> (http) patterns so each agent only declares its deltas.
    /// </summary>
    internal static class AgentConfigBuilders
    {
        /// <summary>
        /// THE single source of truth for the stdio arg values (mcp-authorize b6, design 06): the
        /// per-project <c>port=</c> (<see cref="AgentConfiguratorSettings.PinnedPort"/> — marker
        /// <c>portOverride</c>, else a port typed into <c>Host</c>, else the derived v2 port),
        /// plugin-timeout, client-transport, and the <c>project=&lt;pin&gt;</c> routing arg. NO auth
        /// args — stdio spawns in <c>none</c> mode on the default path (the credential-free /
        /// anonymous local flow).
        ///
        /// <para>Returned as plain strings rather than a <see cref="JsonArray"/> because the emitters
        /// need different containers: <see cref="StdioArgs"/> wraps them in a JSON <c>args</c> array,
        /// <c>CodexConfigurator</c> passes them to a TOML array, and <c>OpenCodeConfigurator</c> prepends
        /// its executable to that same array. Those three used to hand-roll this list independently,
        /// which is exactly how the stdio half of defect A survived the HTTP half's fix — route every new
        /// emitter through here instead.</para>
        ///
        /// <para>The <c>port=</c> arg names <see cref="AgentConfiguratorSettings.PinnedPort"/>, NOT
        /// <see cref="AgentConfiguratorSettings.ResolvedPort"/> — see <c>PinnedPort</c> for the precedence
        /// and for why the writer has to agree with the binder (auth-fixes T1 / defect A, stdio half).</para>
        /// </summary>
        public static string[] StdioArgValues(AgentConfiguratorSettings s) => new[]
        {
            $"{Args.Port}={s.PinnedPort}",
            $"{Args.PluginTimeout}={s.TimeoutMs}",
            $"{Args.ClientTransportMethod}={TransportMethod.stdio}",
            $"{Args.Project}={s.ProjectPin}"
        };

        /// <summary>
        /// <see cref="StdioArgValues"/> as the JSON <c>args</c> array most agents write.
        /// </summary>
        public static JsonArray StdioArgs(AgentConfiguratorSettings s)
        {
            var args = new JsonArray();
            foreach (var arg in StdioArgValues(s))
                args.Add(arg);
            return args;
        }

        /// <summary>
        /// Standard JSON stdio entry: <c>type=stdio</c>, <c>command</c> (path comparison),
        /// <c>args</c>, removing <c>url</c>. When <paramref name="disabled"/> is supplied an
        /// extra required <c>disabled</c> flag is written (some agents — Kilo/Zoo — require it).
        /// </summary>
        public static JsonAiAgentConfig JsonStdio(
            string name,
            string configPath,
            AgentConfiguratorSettings settings,
            ILogger? logger,
            string bodyPath = Consts.MCP.Server.DefaultBodyPath,
            bool? disabled = null)
        {
            var config = new JsonAiAgentConfig(name, configPath, bodyPath, logger)
                .SetProperty("type", JsonValue.Create("stdio")!, requiredForConfiguration: true)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("url");
            if (disabled.HasValue)
                config.SetProperty("disabled", JsonValue.Create(disabled.Value)!, requiredForConfiguration: true);
            return config;
        }

        /// <summary>
        /// Standard JSON http entry: <c>type</c> (default <c>http</c>; some agents require
        /// <c>streamable-http</c>), <c>url</c> (url comparison), removing <c>command</c> +
        /// <c>args</c>. When <paramref name="disabled"/> is supplied an extra required
        /// <c>disabled</c> flag is written.
        /// </summary>
        public static JsonAiAgentConfig JsonHttp(
            string name,
            string configPath,
            AgentConfiguratorSettings settings,
            ILogger? logger,
            string bodyPath = Consts.MCP.Server.DefaultBodyPath,
            string type = "http",
            bool? disabled = null)
        {
            var config = new JsonAiAgentConfig(name, configPath, bodyPath, logger)
                .SetProperty("type", JsonValue.Create(type)!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(settings.PinnedHttpUrl)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");
            if (disabled.HasValue)
                config.SetProperty("disabled", JsonValue.Create(disabled.Value)!, requiredForConfiguration: true);
            return config;
        }
    }
}
