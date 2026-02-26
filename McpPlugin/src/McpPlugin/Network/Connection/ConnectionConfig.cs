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

        /// <summary>
        /// Token for authorization when connecting to the MCP server via SignalR.
        /// Set via command line arg 'mcp-plugin-token' or environment variable 'MCP_PLUGIN_TOKEN'.
        /// </summary>
        public virtual string? Token { get; set; }

        /// <summary>
        /// Whether to automatically generate skill markdown files for each registered MCP tool.
        /// Default is true. Skill files are generated on plugin build and whenever tools are updated.
        /// </summary>
        public virtual bool GenerateSkillFiles { get; set; } = true;

        /// <summary>
        /// Root folder path for generated skill markdown files. Can be absolute or relative to the application base directory.
        /// Default is 'SKILLS'. Set via command line arg 'mcp-skills-folder' or environment variable 'MCP_SKILLS_FOLDER'.
        /// </summary>
        public virtual string SkillsRootFolder { get; set; } = "SKILLS";

        public ConnectionConfig() { }

        public static ConnectionConfig Default => new ConnectionConfig()
        {
            Host = Consts.Hub.DefaultHost,
            TimeoutMs = Consts.Hub.DefaultTimeoutMs
        };

        public static string GetSkillsFolderFromArgsOrEnv(string[]? args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            var folder = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpSkillsFolder);
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            if (commandLineArgs.TryGetValue(Consts.MCP.Plugin.Args.McpSkillsFolder.TrimStart('-'), out var argFolder))
                return argFolder;

            return folder ?? "SKILLS";
        }

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

        public static string? GetTokenFromArgsOrEnv(string[]? args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            var token = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpPluginToken);
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            if (commandLineArgs.TryGetValue(Consts.MCP.Plugin.Args.McpPluginToken.TrimStart('-'), out var argToken))
                return argToken;

            return token;
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

            var token = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpPluginToken);
            if (token != null)
                Token = token;

            var skillsFolder = Environment.GetEnvironmentVariable(Consts.MCP.Plugin.Env.McpSkillsFolder);
            if (skillsFolder != null)
                SkillsRootFolder = skillsFolder;
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

            var argToken = commandLineArgs.GetValueOrDefault(Consts.MCP.Plugin.Args.McpPluginToken.TrimStart('-'));
            if (argToken != null)
                Token = argToken;

            var argSkillsFolder = commandLineArgs.GetValueOrDefault(Consts.MCP.Plugin.Args.McpSkillsFolder.TrimStart('-'));
            if (argSkillsFolder != null)
                SkillsRootFolder = argSkillsFolder;
        }

        public override string ToString()
            => $"Endpoint: {Host}, Timeout: {TimeoutMs}ms";
    }
}
