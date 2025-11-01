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
        public virtual string Host { get; set; } = Consts.Hub.DefaultHost;

        /// <summary>
        /// Timeout in milliseconds for MCP operations. This is set at runtime via command line args or environment variables.
        /// </summary>
        public virtual int TimeoutMs { get; set; } = Consts.Hub.DefaultTimeoutMs;
        public virtual bool KeepConnected { get; set; } = true;

        public ConnectionConfig() { }

        public static ConnectionConfig Default => new ConnectionConfig()
        {
            Host = Consts.Hub.DefaultHost,
            TimeoutMs = Consts.Hub.DefaultTimeoutMs
        };

        public static ConnectionConfig BuildFromArgsOrEnv(string[]? args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            var config = new ConnectionConfig();
            config.ParseEnvironmentVariables();
            config.ParseCommandLineArguments(args);
            return config;
        }

        public static string GetEndpointFromArgsOrEnv(string[]? args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            var endpoint = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpServerEndpoint);
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            if (commandLineArgs.TryGetValue(Consts.MCP.Plugin.Args.McpServerEndpoint.TrimStart('-'), out var argEndpoint))
                return argEndpoint;

            return endpoint ?? Consts.Hub.DefaultHost;
        }

        public static int GetTimeoutFromArgsOrEnv(string[]? args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            var timeoutStr = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpServerTimeout);
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            if (commandLineArgs.TryGetValue(Consts.MCP.Plugin.Args.McpServerTimeout.TrimStart('-'), out var argTimeout))
            {
                if (int.TryParse(argTimeout, out var timeoutFromArgs))
                    return timeoutFromArgs;
            }

            if (timeoutStr != null && int.TryParse(timeoutStr, out var timeoutFromEnv))
                return timeoutFromEnv;

            return Consts.Hub.DefaultTimeoutMs;
        }

        void ParseEnvironmentVariables()
        {
            // --- Global variables ---

            var endpoint = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpServerEndpoint);
            if (endpoint != null)
                Host = endpoint;

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
                Host = argPort;

            // --- Plugin variables ---

            var argPluginTimeout = commandLineArgs.GetValueOrDefault(Consts.MCP.Plugin.Args.McpServerTimeout.TrimStart('-'));
            if (argPluginTimeout != null && int.TryParse(argPluginTimeout, out var timeoutMs))
                TimeoutMs = timeoutMs;
        }

        public override string ToString()
            => $"Endpoint: {Host}, Timeout: {TimeoutMs}ms";
    }
}
