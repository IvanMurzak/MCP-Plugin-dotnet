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
        /// <c>args</c>, removing <c>url</c>.
        /// </summary>
        public static JsonAiAgentConfig JsonStdio(
            string name,
            string configPath,
            AgentConfiguratorSettings settings,
            ILogger? logger,
            string bodyPath = Consts.MCP.Server.DefaultBodyPath)
        {
            return new JsonAiAgentConfig(name, configPath, bodyPath, logger)
                .SetProperty("type", JsonValue.Create("stdio")!, requiredForConfiguration: true)
                .SetProperty("command", JsonValue.Create(settings.ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .SetProperty("args", StdioArgs(settings), requiredForConfiguration: true)
                .SetPropertyToRemove("url");
        }

        /// <summary>
        /// Standard JSON http entry: <c>type=http</c>, <c>url</c> (url comparison),
        /// removing <c>command</c> + <c>args</c>.
        /// </summary>
        public static JsonAiAgentConfig JsonHttp(
            string name,
            string configPath,
            AgentConfiguratorSettings settings,
            ILogger? logger,
            string bodyPath = Consts.MCP.Server.DefaultBodyPath)
        {
            return new JsonAiAgentConfig(name, configPath, bodyPath, logger)
                .SetProperty("type", JsonValue.Create("http")!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(settings.Host)!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");
        }
    }
}
