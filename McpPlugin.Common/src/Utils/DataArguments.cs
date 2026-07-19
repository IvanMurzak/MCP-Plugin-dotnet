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
        int IdleTimeoutSeconds { get; }
        int MaxIdleSessionCount { get; }
        Consts.MCP.Server.TransportMethod ClientTransport { get; }
        Consts.MCP.Server.AuthOption Authorization { get; }
        string? Token { get; }

        // OAuth resource-server configuration (mcp-authorize b2)
        string? AuthIssuer { get; }
        string? PublicUrl { get; }

        // Optional server-side metadata / fetch-base override (auth-fixes L2a / Gap B). Null unless set.
        string? AuthMetadataUrl { get; }
        string? Bind { get; }

        // Session project pin for stdio account routing (mcp-authorize b3, design 04 D14)
        string? ProjectPin { get; }

        // Webhook configuration
        string? WebhookToolUrl { get; }
        string? WebhookPromptUrl { get; }
        string? WebhookResourceUrl { get; }
        string? WebhookConnectionUrl { get; }
        string? WebhookToken { get; }
        string? WebhookHeader { get; }
        int WebhookTimeoutMs { get; }

        // Authorization webhook configuration
        string? WebhookAuthorizationUrl { get; }
        bool WebhookAuthorizationFailOpen { get; }
    }
    public class DataArguments : IDataArguments
    {
        public int Port { get; private set; } = 8080;
        public int PluginTimeoutMs { get; private set; }
        public int IdleTimeoutSeconds { get; private set; } = Consts.MCP.Server.DefaultIdleTimeoutSeconds;
        public int MaxIdleSessionCount { get; private set; } = Consts.MCP.Server.DefaultMaxIdleSessionCount;
        public Consts.MCP.Server.TransportMethod ClientTransport { get; private set; }
        public Consts.MCP.Server.AuthOption Authorization { get; private set; } = Consts.MCP.Server.AuthOption.none;
        public string? Token { get; private set; }

        // OAuth resource-server configuration (mcp-authorize b2)
        public string? AuthIssuer { get; private set; }
        public string? PublicUrl { get; private set; }

        /// <summary>
        /// Optional server-side metadata / fetch-base override (auth-fixes L2a / Gap B). When set, the
        /// OAuth resource server fetches JWKS / introspection / enrollment from this
        /// base instead of <see cref="AuthIssuer"/>; the token <c>iss</c> claim check and the RFC 9728
        /// PRM <c>authorization_servers</c> stay on <see cref="AuthIssuer"/> (client-facing). Null
        /// (default, incl. all of prod) → behavior is byte-identical to deriving from the issuer. Set
        /// via <c>--auth-metadata-url</c> / <c>MCP_AUTH_METADATA_URL</c> for a fully-local OAuth
        /// deployment where the client-facing issuer host is unreachable from inside the RS container.
        /// </summary>
        public string? AuthMetadataUrl { get; private set; }

        /// <summary>
        /// Bind address for the streamableHttp Kestrel listener. <c>null</c>/empty defaults to
        /// <b>loopback</b> (decision D8 — the local RS is loopback-only by default; DNS-rebinding
        /// is still blocked by Origin validation). Set <c>--bind any</c> / <c>--bind 0.0.0.0</c>
        /// (or a specific IP) to opt into LAN / container exposure.
        /// </summary>
        public string? Bind { get; private set; }

        /// <summary>
        /// The session's project pin (mcp-authorize b3, design 04 D14) supplied on a stdio spawn as
        /// <c>project=&lt;pin&gt;</c>. Pins the session's routing to instances whose project path hash
        /// matches — never another project. Null when unset. The streamableHttp equivalent is the
        /// <c>/p/&lt;pin&gt;</c> URL path segment (captured per-request by the session middleware).
        /// </summary>
        public string? ProjectPin { get; private set; }

        // Webhook configuration
        public string? WebhookToolUrl { get; private set; }
        public string? WebhookPromptUrl { get; private set; }
        public string? WebhookResourceUrl { get; private set; }
        public string? WebhookConnectionUrl { get; private set; }
        public string? WebhookToken { get; private set; }
        public string? WebhookHeader { get; private set; }
        public int WebhookTimeoutMs { get; private set; } = 10000;

        // Authorization webhook configuration
        public string? WebhookAuthorizationUrl { get; private set; }
        public bool WebhookAuthorizationFailOpen { get; private set; } = false;

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

            var envIdleTimeoutSeconds = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds);
            if (envIdleTimeoutSeconds != null && int.TryParse(envIdleTimeoutSeconds, out var parsedEnvIdleTimeoutSeconds) && parsedEnvIdleTimeoutSeconds > 0)
                IdleTimeoutSeconds = parsedEnvIdleTimeoutSeconds;

            var envMaxIdleSessionCount = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount);
            if (envMaxIdleSessionCount != null && int.TryParse(envMaxIdleSessionCount, out var parsedEnvMaxIdleSessionCount) && parsedEnvMaxIdleSessionCount > 0)
                MaxIdleSessionCount = parsedEnvMaxIdleSessionCount;

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

            // MCP_AUTH is the target-state name for MCP_AUTHORIZATION; parsed after it so it wins.
            var envAuth = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.Auth);
            if (envAuth != null && Enum.TryParse(envAuth, true, out Consts.MCP.Server.AuthOption parsedEnvAuth))
                Authorization = parsedEnvAuth;

            // --- OAuth resource-server variables ---

            var envAuthIssuer = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.AuthIssuer);
            if (envAuthIssuer != null)
                AuthIssuer = envAuthIssuer;

            var envPublicUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.PublicUrl);
            if (envPublicUrl != null)
                PublicUrl = envPublicUrl;

            var envAuthMetadataUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.AuthMetadataUrl);
            if (envAuthMetadataUrl != null)
                AuthMetadataUrl = envAuthMetadataUrl;

            var envBind = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.Bind);
            if (envBind != null)
                Bind = envBind;

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

            // --- Authorization webhook variables ---

            var envWebhookAuthorizationUrl = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookAuthorizationUrl);
            if (envWebhookAuthorizationUrl != null)
                WebhookAuthorizationUrl = envWebhookAuthorizationUrl;

            var envWebhookAuthorizationFailOpen = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.WebhookAuthorizationFailOpen);
            if (envWebhookAuthorizationFailOpen != null && bool.TryParse(envWebhookAuthorizationFailOpen, out var parsedEnvWebhookAuthorizationFailOpen))
                WebhookAuthorizationFailOpen = parsedEnvWebhookAuthorizationFailOpen;
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

            var argIdleTimeoutSeconds = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.IdleTimeoutSeconds.TrimStart('-'));
            if (argIdleTimeoutSeconds != null && int.TryParse(argIdleTimeoutSeconds, out var parsedArgIdleTimeoutSeconds) && parsedArgIdleTimeoutSeconds > 0)
                IdleTimeoutSeconds = parsedArgIdleTimeoutSeconds;

            var argMaxIdleSessionCount = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.MaxIdleSessionCount.TrimStart('-'));
            if (argMaxIdleSessionCount != null && int.TryParse(argMaxIdleSessionCount, out var parsedArgMaxIdleSessionCount) && parsedArgMaxIdleSessionCount > 0)
                MaxIdleSessionCount = parsedArgMaxIdleSessionCount;

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

            // --auth is the target-state name for --authorization; parsed after it so it wins.
            var argAuth = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Auth.TrimStart('-'));
            if (argAuth != null && Enum.TryParse(argAuth, true, out Consts.MCP.Server.AuthOption parsedArgAuth))
                Authorization = parsedArgAuth;

            // --- OAuth resource-server variables ---

            var argAuthIssuer = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.AuthIssuer.TrimStart('-'));
            if (argAuthIssuer != null)
                AuthIssuer = argAuthIssuer;

            var argPublicUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.PublicUrl.TrimStart('-'));
            if (argPublicUrl != null)
                PublicUrl = argPublicUrl;

            var argAuthMetadataUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.AuthMetadataUrl.TrimStart('-'));
            if (argAuthMetadataUrl != null)
                AuthMetadataUrl = argAuthMetadataUrl;

            var argBind = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Bind.TrimStart('-'));
            if (argBind != null)
                Bind = argBind;

            // --- Session project pin (stdio account routing, mcp-authorize b3) ---

            var argProject = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.Project.TrimStart('-'));
            if (!string.IsNullOrEmpty(argProject))
                ProjectPin = argProject!.Trim().ToLowerInvariant();

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

            // --- Authorization webhook variables ---

            var argWebhookAuthorizationUrl = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookAuthorizationUrl.TrimStart('-'));
            if (argWebhookAuthorizationUrl != null)
                WebhookAuthorizationUrl = argWebhookAuthorizationUrl;

            var argWebhookAuthorizationFailOpen = commandLineArgs.GetValueOrDefault(Consts.MCP.Server.Args.WebhookAuthorizationFailOpen.TrimStart('-'));
            if (argWebhookAuthorizationFailOpen != null && bool.TryParse(argWebhookAuthorizationFailOpen, out var parsedArgWebhookAuthorizationFailOpen))
                WebhookAuthorizationFailOpen = parsedArgWebhookAuthorizationFailOpen;
        }
    }
}
