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
using com.IvanMurzak.McpPlugin.Common;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// Builds the Docker setup / start / stop / remove commands the Custom configurator surfaces
    /// for running the shared MCP server in a container. Engine-agnostic port of Unity's
    /// <c>McpServerManager.DockerSetupRunCommand / DockerRunCommand / DockerStopCommand /
    /// DockerRemoveCommand</c>; the container name, image, and pinned version come from
    /// <see cref="AgentConfiguratorSettings"/> so each engine can override them.
    /// </summary>
    public static class DockerCommands
    {
        /// <summary>The container name (<c>{ServerExecutableName}-{Port}</c>) the commands operate on.</summary>
        public static string ContainerName(AgentConfiguratorSettings settings)
            => $"{settings.ServerExecutableName}-{settings.Port}";

        /// <summary>
        /// First-time setup: <c>docker run -d</c> with port mapping, env vars (transport, port,
        /// plugin-timeout, authorization, optional token), container name, and the pinned image.
        /// </summary>
        public static string SetupRun(AgentConfiguratorSettings settings)
        {
            var dockerPortMapping = $"-p {settings.Port}:{settings.Port}";
            var dockerEnvVars =
                $"-e {Env.ClientTransportMethod}={TransportMethod.streamableHttp} " +
                $"-e {Env.Port}={settings.Port} " +
                $"-e {Env.PluginTimeout}={settings.TimeoutMs} " +
                $"-e {Env.Authorization}={settings.AuthOption}";

            if (settings.AuthOption == AuthOption.required && !string.IsNullOrEmpty(settings.Token))
                dockerEnvVars += $" -e {Env.Token}={settings.Token}";

            var dockerContainer = $"--name {ContainerName(settings)}";
            // The shared GameDev-MCP-Server Docker image, tagged by the pinned ServerVersion
            // (NOT the plugin version — the two diverge).
            var dockerImage = $"{settings.DockerImage}:{settings.ServerVersion}";
            return $"docker run -d {dockerPortMapping} {dockerEnvVars} {dockerContainer} {dockerImage}";
        }

        /// <summary>Subsequent start of the already-created container.</summary>
        public static string Run(AgentConfiguratorSettings settings)
            => $"docker start {ContainerName(settings)}";

        /// <summary>Stop the running container.</summary>
        public static string Stop(AgentConfiguratorSettings settings)
            => $"docker stop {ContainerName(settings)}";

        /// <summary>Remove the (stopped) container.</summary>
        public static string Remove(AgentConfiguratorSettings settings)
            => $"docker rm {ContainerName(settings)}";
    }
}
