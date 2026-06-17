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
using System.Runtime.InteropServices;
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The operating-system family a configurator builds a config for. Engine-agnostic
    /// replacement for the <c>#if UNITY_EDITOR_WIN</c> / <c>#if UNITY_EDITOR_OSX</c>
    /// compile-time branches in the original Unity implementation. The consuming engine
    /// detects the host OS and passes the matching value.
    /// </summary>
    public enum OperatingSystemKind
    {
        Windows,
        MacOS,
        Linux
    }

    /// <summary>
    /// Runtime host-OS detection. The engine-agnostic replacement for Unity's compile-time
    /// <c>#if UNITY_EDITOR_WIN</c> / <c>#if UNITY_EDITOR_OSX</c> / <c>#if UNITY_EDITOR_LINUX</c>
    /// selection. Consumers that do not know (or do not want to hard-code) the host OS get the
    /// correct value automatically; tests and engine consumers can still force a specific value
    /// via the explicit <see cref="AgentConfiguratorSettings"/> constructor.
    /// </summary>
    public static class HostOperatingSystem
    {
        /// <summary>
        /// Detects the operating system the current process is running on using
        /// <see cref="RuntimeInformation.IsOSPlatform"/>. Windows / OSX (→ <see cref="OperatingSystemKind.MacOS"/>)
        /// / Linux are recognised; anything else falls back to <see cref="OperatingSystemKind.Linux"/>
        /// (Unix-like path conventions).
        /// </summary>
        public static OperatingSystemKind Detect()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return OperatingSystemKind.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return OperatingSystemKind.MacOS;
            return OperatingSystemKind.Linux;
        }
    }

    /// <summary>
    /// Where the plugin connects: a locally launched server or the ai-game.dev cloud.
    /// Engine-agnostic replacement for the Unity editor's <c>ConnectionMode</c>. In Cloud
    /// mode authorization is always required (the cloud server enforces it).
    /// </summary>
    public enum ConnectionMode
    {
        Local,
        Cloud
    }

    /// <summary>
    /// All the engine-supplied values a configurator needs to build an MCP config entry.
    /// Replaces the Unity statics (<c>UnityMcpPluginEditor.Port</c>, <c>.Host</c>,
    /// <c>.Token</c>, …, and <c>McpServerManager.ExecutableFullPath</c>) the original
    /// implementation read from. The consuming engine constructs this and hands it to the
    /// registry / configurators; nothing here touches an editor UI or engine runtime type.
    /// </summary>
    public sealed class AgentConfiguratorSettings
    {
        /// <summary>Host OS the config is being generated for (drives per-OS config-file locations).</summary>
        public OperatingSystemKind OperatingSystem { get; }

        /// <summary>Absolute path to the consuming project's root (where project-local config files live).</summary>
        public string ProjectRootPath { get; }

        /// <summary>Absolute path to the MCP server executable (stdio transport <c>command</c>).</summary>
        public string ExecutableFullPath { get; }

        /// <summary>The MCP server port (stdio transport arg).</summary>
        public int Port { get; }

        /// <summary>Plugin timeout in milliseconds (stdio transport arg).</summary>
        public int TimeoutMs { get; }

        /// <summary>The streamableHttp server URL (http transport <c>url</c>).</summary>
        public string Host { get; }

        /// <summary>Bearer token, or empty/null when auth is not configured.</summary>
        public string? Token { get; }

        /// <summary>Whether the plugin connects to a local server or the cloud.</summary>
        public ConnectionMode ConnectionMode { get; }

        /// <summary>Whether bearer-token auth is required (independent of cloud enforcement).</summary>
        public Consts.MCP.Server.AuthOption AuthOption { get; }

        /// <summary>
        /// Base name of the shared MCP server executable / Docker container, used to build the
        /// Docker container name (<c>{ServerExecutableName}-{Port}</c>). Defaults to Unity's
        /// <c>gamedev-mcp-server</c>; engines that ship a differently-named server override it.
        /// </summary>
        public string ServerExecutableName { get; }

        /// <summary>
        /// The pinned shared MCP server version, used as the Docker image tag
        /// (<c>{DockerImage}:{ServerVersion}</c>). Engines pin the server release they download;
        /// defaults to Unity's currently-pinned <c>8.0.0</c>.
        /// </summary>
        public string ServerVersion { get; }

        /// <summary>
        /// The Docker image repository for the shared MCP server. Defaults to the published
        /// <c>aigamedeveloper/mcp-server</c> image.
        /// </summary>
        public string DockerImage { get; }

        public AgentConfiguratorSettings(
            OperatingSystemKind operatingSystem,
            string projectRootPath,
            string executableFullPath,
            int port,
            int timeoutMs,
            string host,
            string? token = null,
            ConnectionMode connectionMode = ConnectionMode.Local,
            Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none,
            string serverExecutableName = "gamedev-mcp-server",
            string serverVersion = "8.0.0",
            string dockerImage = "aigamedeveloper/mcp-server")
        {
            OperatingSystem = operatingSystem;
            ProjectRootPath = projectRootPath;
            ExecutableFullPath = executableFullPath;
            Port = port;
            TimeoutMs = timeoutMs;
            Host = host;
            Token = token;
            ConnectionMode = connectionMode;
            AuthOption = authOption;
            ServerExecutableName = serverExecutableName;
            ServerVersion = serverVersion;
            DockerImage = dockerImage;
        }

        /// <summary>
        /// Creates settings that auto-detect the host OS at runtime (<see cref="HostOperatingSystem.Detect"/>).
        /// This is the recommended path for consumers that simply want correct per-OS config-file
        /// locations on the machine the process runs on — without hand-detecting and injecting the OS.
        /// Use the OS-explicit constructor only when a specific <see cref="OperatingSystemKind"/> must be forced
        /// (e.g. tests, or generating config for a different OS than the host).
        /// </summary>
        public static AgentConfiguratorSettings CreateForHost(
            string projectRootPath,
            string executableFullPath,
            int port,
            int timeoutMs,
            string host,
            string? token = null,
            ConnectionMode connectionMode = ConnectionMode.Local,
            Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none,
            string serverExecutableName = "gamedev-mcp-server",
            string serverVersion = "8.0.0",
            string dockerImage = "aigamedeveloper/mcp-server")
        {
            return new AgentConfiguratorSettings(
                operatingSystem: HostOperatingSystem.Detect(),
                projectRootPath: projectRootPath,
                executableFullPath: executableFullPath,
                port: port,
                timeoutMs: timeoutMs,
                host: host,
                token: token,
                connectionMode: connectionMode,
                authOption: authOption,
                serverExecutableName: serverExecutableName,
                serverVersion: serverVersion,
                dockerImage: dockerImage);
        }

        /// <summary>True when HTTP authorization should be injected (cloud always requires it).</summary>
        public bool IsHttpAuthRequired =>
            ConnectionMode == ConnectionMode.Cloud || AuthOption == Consts.MCP.Server.AuthOption.required;

        /// <summary>True when STDIO authorization (token arg) should be injected.</summary>
        public bool IsStdioAuthRequired =>
            AuthOption == Consts.MCP.Server.AuthOption.required;

        /// <summary>Convenience: <see cref="OperatingSystem"/> is <see cref="OperatingSystemKind.Windows"/>.</summary>
        public bool IsWindows => OperatingSystem == OperatingSystemKind.Windows;
    }
}
