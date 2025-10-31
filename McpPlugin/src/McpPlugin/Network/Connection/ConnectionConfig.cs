/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System;
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;

namespace com.IvanMurzak.McpPlugin
{
    public class ConnectionConfig
    {
        public string ServerUrl { get; set; } = Consts.Hub.DefaultEndpoint;

        /// <summary>
        /// Timeout in milliseconds for MCP operations. This is set at runtime via command line args or environment variables.
        /// </summary>
        public int TimeoutMs { get; set; } = Consts.Hub.DefaultTimeoutMs;

        public ConnectionConfig() { }

        public static ConnectionConfig Default => new ConnectionConfig()
        {
            ServerUrl = Consts.Hub.DefaultEndpoint,
            TimeoutMs = Consts.Hub.DefaultTimeoutMs
        };

        public static ConnectionConfig BuildFromArgsOrEnv(string[] args)
        {
            var config = new ConnectionConfig();
            config.ParseEnvironmentVariables();
            config.ParseCommandLineArguments(args);
            return config;
        }

        void ParseEnvironmentVariables()
        {
            // --- Global variables ---

            var endpoint = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpServerEndpoint);
            if (endpoint != null)
                ServerUrl = endpoint;

            // --- Plugin variables ---

            var timeout = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpServerTimeout);
            if (timeout != null && int.TryParse(timeout, out var parsedEnvTimeoutMs))
                TimeoutMs = parsedEnvTimeoutMs;
        }
        void ParseCommandLineArguments(string[] args)
        {
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            // --- Global variables ---

            var argPort = commandLineArgs.GetValueOrDefault(Consts.MCP.Plugin.Args.McpServerEndpoint.TrimStart('-'));
            if (argPort != null)
                ServerUrl = argPort;

            // --- Plugin variables ---

            var argPluginTimeout = commandLineArgs.GetValueOrDefault(Consts.MCP.Plugin.Args.McpServerTimeout.TrimStart('-'));
            if (argPluginTimeout != null && int.TryParse(argPluginTimeout, out var timeoutMs))
                TimeoutMs = timeoutMs;
        }

        public override string ToString()
            => $"Endpoint: {ServerUrl}, Timeout: {TimeoutMs}ms";
    }
}
