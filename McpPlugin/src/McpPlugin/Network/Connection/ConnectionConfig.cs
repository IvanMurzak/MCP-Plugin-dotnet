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
using System.Text.Json.Serialization;
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
        /// Maximum number of CONSECUTIVE connection-attempt failures (endpoint unreachable / negotiate timeout)
        /// before the reconnect loop GIVES UP (settles into idle-Disconnected; reconnect via Connect once the
        /// server is reachable). <c>0</c> (the default) means UNLIMITED — retry forever, the historical behaviour
        /// for Unity/Unreal hosts. Set a positive value to OPT IN to bounded reconnect: required by consumers
        /// hosted in a COLLECTIBLE AssemblyLoadContext (the Godot editor addon), where a perpetual in-flight
        /// negotiate is a hot-reload pin (godotengine/godot#78513). Mirrors the always-on auth-rejection cap.
        /// </summary>
        public virtual int MaxConsecutiveConnectionFailures { get; set; } = 0;

        /// <summary>
        /// Transport CONNECT timeout in seconds (SocketsHttpHandler.ConnectTimeout). <c>0</c> (the default) leaves
        /// the framework default (~30s) — the historical behaviour. Set a positive value to make an unreachable
        /// endpoint fail FAST instead of hanging on the OS TCP connect, so <see cref="MaxConsecutiveConnectionFailures"/>
        /// is reached promptly. Reachable servers connect well under any sane value, so they are unaffected.
        /// .NET-Core-only (the netstandard2.1 / Unity target has no SocketsHttpHandler and ignores this).
        /// </summary>
        public virtual int ConnectTimeoutSeconds { get; set; } = 0;

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
        /// Path for generated skill markdown files. Can be absolute or relative.
        /// When relative, it is anchored against (in priority order): the <c>basePath</c> argument
        /// passed to <see cref="IMcpPlugin.GenerateSkillFiles(string?)"/> /
        /// <see cref="IMcpPlugin.DeleteSkillFiles(string?)"/>; otherwise <see cref="ProjectRootPath"/>;
        /// otherwise the resolver throws. There is no silent fallback to the host process's
        /// current working directory — see GitHub issue #107.
        /// Default is 'SKILLS'. Set via command line arg 'mcp-skills-folder' or environment variable 'MCP_SKILLS_FOLDER'.
        /// </summary>
        public virtual string SkillsPath { get; set; } = "SKILLS";

        /// <summary>
        /// Absolute filesystem path to the host project root — the folder that relative
        /// <see cref="SkillsPath"/> values are anchored against. Runtime-only; MUST NOT be
        /// serialized to disk (see <see cref="JsonIgnoreAttribute"/>) so that the on-disk
        /// connection config remains portable across machines (Unity-MCP issue #761).
        /// Hosts SHOULD set this once at plugin construction — e.g. Unity sets it to the
        /// parent of <c>Application.dataPath</c>. When null, callers MUST pass an explicit
        /// <c>basePath</c> to any API that resolves a relative <see cref="SkillsPath"/>;
        /// otherwise that API throws an <see cref="InvalidOperationException"/>.
        /// </summary>
        [JsonIgnore]
        public string? ProjectRootPath { get; set; }

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
                SkillsPath = skillsFolder;
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
                SkillsPath = argSkillsFolder;
        }

        public override string ToString()
            => $"Endpoint: {Host}, Timeout: {TimeoutMs}ms";
    }
}
