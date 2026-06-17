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
        /// <summary>The standard stdio arg array (port, plugin-timeout, client-transport, authorization).</summary>
        public static JsonArray StdioArgs(AgentConfiguratorSettings s) => new()
        {
            $"{Args.Port}={s.Port}",
            $"{Args.PluginTimeout}={s.TimeoutMs}",
            $"{Args.ClientTransportMethod}={TransportMethod.stdio}",
            $"{Args.Authorization}={s.AuthOption}"
        };

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
                .SetProperty("url", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");
            if (disabled.HasValue)
                config.SetProperty("disabled", JsonValue.Create(disabled.Value)!, requiredForConfiguration: true);
            return config;
        }
    }
}
