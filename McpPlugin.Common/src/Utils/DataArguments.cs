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

namespace com.IvanMurzak.McpPlugin.Common.Utils
{
    public interface IDataArguments
    {
        int Port { get; }
        int PluginTimeoutMs { get; }
        Consts.MCP.Server.TransportMethod ClientTransport { get; }
        Consts.MCP.Server.AuthOption Authorization { get; }
        string? Token { get; }
    }
    public class DataArguments : IDataArguments
    {
        public int Port { get; private set; } = 8080;
        public int PluginTimeoutMs { get; private set; }
        public Consts.MCP.Server.TransportMethod ClientTransport { get; private set; }
        public Consts.MCP.Server.AuthOption Authorization { get; private set; }
        public string? Token { get; private set; }

        public DataArguments(string[] args)
        {
            Port = Consts.Hub.DefaultPort;
            PluginTimeoutMs = Consts.Hub.DefaultTimeoutMs;
            ClientTransport = Consts.MCP.Server.TransportMethod.streamableHttp;

            ParseEnvironmentVariables(); // env variables - second priority
            ParseCommandLineArguments(args); // command line args - first priority (override previous values)

            // Auto-detect deployment mode if not explicitly set
            if (Authorization == Consts.MCP.Server.AuthOption.unknown)
                Authorization = Consts.MCP.Server.AuthOption.none;
        }

        void ParseEnvironmentVariables()
        {
            // --- Global variables ---

            var envPort = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.Port);
            if (envPort != null && int.TryParse(envPort, out var parsedEnvPort))
                Port = parsedEnvPort;

            // --- Plugin variables ---

            var envPluginTimeout = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.PluginTimeout);
            if (envPluginTimeout != null && int.TryParse(envPluginTimeout, out var parsedEnvTimeoutMs))
                PluginTimeoutMs = parsedEnvTimeoutMs;

            // --- Client variables ---

            var envClientTransport = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.ClientTransportMethod);
            if (envClientTransport != null && Enum.TryParse(envClientTransport, out Consts.MCP.Server.TransportMethod parsedEnvTransport))
                ClientTransport = parsedEnvTransport;

            // --- Token ---

            var envToken = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.Token);
            if (envToken != null)
                Token = envToken;

            // --- Deployment mode ---

            var envDeployment = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.Authorization);
            if (envDeployment != null && Enum.TryParse(envDeployment, true, out Consts.MCP.Server.AuthOption parsedEnvDeployment))
                Authorization = parsedEnvDeployment;
        }
        void ParseCommandLineArguments(string[] args)
        {
            var commandLineArgs = ArgsUtils.ParseLineArguments(args);

            // --- Global variables ---

            var argPort = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Port.TrimStart('-'));
            if (argPort != null && int.TryParse(argPort, out var port))
                Port = port;

            // --- Plugin variables ---

            var argPluginTimeout = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.PluginTimeout.TrimStart('-'));
            if (argPluginTimeout != null && int.TryParse(argPluginTimeout, out var timeoutMs))
                PluginTimeoutMs = timeoutMs;

            // --- Client variables ---

            var argClientTransport = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.ClientTransportMethod.TrimStart('-'));
            if (argClientTransport != null && Enum.TryParse(argClientTransport, out Consts.MCP.Server.TransportMethod parsedArgTransport))
                ClientTransport = parsedArgTransport;

            // --- Token ---

            var argToken = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Token.TrimStart('-'));
            if (argToken != null)
                Token = argToken;

            // --- Deployment mode ---

            var argDeployment = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Authorization.TrimStart('-'));
            if (argDeployment != null && Enum.TryParse(argDeployment, true, out Consts.MCP.Server.AuthOption parsedArgDeployment))
                Authorization = parsedArgDeployment;
        }
    }
}