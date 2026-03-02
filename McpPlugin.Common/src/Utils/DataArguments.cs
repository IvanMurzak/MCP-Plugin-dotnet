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

        // Webhook configuration
        string? WebhookToolUrl { get; }
        string? WebhookPromptUrl { get; }
        string? WebhookResourceUrl { get; }
        string? WebhookConnectionUrl { get; }
        string? WebhookToken { get; }
        string? WebhookHeader { get; }
        int WebhookTimeoutMs { get; }
    }
    public class DataArguments : IDataArguments
    {
        public int Port { get; private set; } = 8080;
        public int PluginTimeoutMs { get; private set; }
        public Consts.MCP.Server.TransportMethod ClientTransport { get; private set; }
        public Consts.MCP.Server.AuthOption Authorization { get; private set; } = Consts.MCP.Server.AuthOption.none;
        public string? Token { get; private set; }

        // Webhook configuration
        public string? WebhookToolUrl { get; private set; }
        public string? WebhookPromptUrl { get; private set; }
        public string? WebhookResourceUrl { get; private set; }
        public string? WebhookConnectionUrl { get; private set; }
        public string? WebhookToken { get; private set; }
        public string? WebhookHeader { get; private set; }
        public int WebhookTimeoutMs { get; private set; } = 10000;

        public DataArguments(string[] args)
        {
            Port = Consts.Hub.DefaultPort;
            PluginTimeoutMs = Consts.Hub.DefaultTimeoutMs;
            ClientTransport = Consts.MCP.Server.TransportMethod.streamableHttp;

            ParseEnvironmentVariables(); // env variables - second priority
            ParseCommandLineArguments(args); // command line args - first priority (override previous values)

            // Default to 'none' authorization if not explicitly set
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

            // --- Webhook variables ---

            var envWebhookToolUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookToolUrl);
            if (envWebhookToolUrl != null)
                WebhookToolUrl = envWebhookToolUrl;

            var envWebhookPromptUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookPromptUrl);
            if (envWebhookPromptUrl != null)
                WebhookPromptUrl = envWebhookPromptUrl;

            var envWebhookResourceUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookResourceUrl);
            if (envWebhookResourceUrl != null)
                WebhookResourceUrl = envWebhookResourceUrl;

            var envWebhookConnectionUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookConnectionUrl);
            if (envWebhookConnectionUrl != null)
                WebhookConnectionUrl = envWebhookConnectionUrl;

            var envWebhookToken = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookToken);
            if (envWebhookToken != null)
                WebhookToken = envWebhookToken;

            var envWebhookHeader = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookHeader);
            if (envWebhookHeader != null)
                WebhookHeader = envWebhookHeader;

            var envWebhookTimeout = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookTimeout);
            if (envWebhookTimeout != null && int.TryParse(envWebhookTimeout, out var parsedEnvWebhookTimeout))
                WebhookTimeoutMs = parsedEnvWebhookTimeout;
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

            // --- Webhook variables ---

            var argWebhookToolUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookToolUrl.TrimStart('-'));
            if (argWebhookToolUrl != null)
                WebhookToolUrl = argWebhookToolUrl;

            var argWebhookPromptUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookPromptUrl.TrimStart('-'));
            if (argWebhookPromptUrl != null)
                WebhookPromptUrl = argWebhookPromptUrl;

            var argWebhookResourceUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookResourceUrl.TrimStart('-'));
            if (argWebhookResourceUrl != null)
                WebhookResourceUrl = argWebhookResourceUrl;

            var argWebhookConnectionUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookConnectionUrl.TrimStart('-'));
            if (argWebhookConnectionUrl != null)
                WebhookConnectionUrl = argWebhookConnectionUrl;

            var argWebhookToken = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookToken.TrimStart('-'));
            if (argWebhookToken != null)
                WebhookToken = argWebhookToken;

            var argWebhookHeader = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookHeader.TrimStart('-'));
            if (argWebhookHeader != null)
                WebhookHeader = argWebhookHeader;

            var argWebhookTimeout = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookTimeout.TrimStart('-'));
            if (argWebhookTimeout != null && int.TryParse(argWebhookTimeout, out var parsedArgWebhookTimeout))
                WebhookTimeoutMs = parsedArgWebhookTimeout;
        }
    }
}