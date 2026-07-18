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
using System;
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

        /// <summary>
        /// True when HTTP authorization should be injected — i.e. the client config must carry an
        /// <c>Authorization: Bearer &lt;secret&gt;</c> header. Cloud always requires it; the offline
        /// <c>token</c> mode requires it (mcp-authorize g6, HTTP only); the deprecated <c>required</c>
        /// alias keeps it for back-compat. <c>none</c> and <c>oauth</c> stay URL-only (oauth authorizes
        /// natively against the server URL; none is anonymous).
        /// </summary>
        public bool IsHttpAuthRequired =>
            ConnectionMode == ConnectionMode.Cloud
            || AuthOption == Consts.MCP.Server.AuthOption.token
            || AuthOption == Consts.MCP.Server.AuthOption.required;

        /// <summary>
        /// True when STDIO authorization (token arg) should be injected. Only the deprecated
        /// <c>required</c> mode ever gated stdio; the offline <c>token</c> mode is HTTP-only —
        /// stdio spawns stay credential-free (mcp-authorize b6 / g6).
        /// </summary>
        public bool IsStdioAuthRequired =>
            AuthOption == Consts.MCP.Server.AuthOption.required;

        /// <summary>
        /// The <see cref="HttpCredentialMode"/> the HTTP config writer uses for these settings
        /// (mcp-authorize g5/g6). A LOCAL server in the offline <c>token</c> mode is Bearer-gated, so
        /// its client config MUST carry the <c>Authorization: Bearer &lt;local-secret&gt;</c> header
        /// (<see cref="HttpCredentialMode.AccessToken"/>). Every other case — <c>none</c>, <c>oauth</c>,
        /// and Cloud — keeps the default credential-free OAuth path (URL-only; the client authorizes
        /// natively against the server URL). Single source of truth shared across engines so Unity /
        /// Godot / Unreal resolve the credential mode identically — the "expected" config a status
        /// check builds always matches what Configure writes. Pure; hoisted from Unity's former
        /// <c>AiAgentConfiguratorView.ResolveHttpCredentialMode</c> (mcp-authorize i1).
        /// </summary>
        public HttpCredentialMode ResolveHttpCredentialMode() =>
            ConnectionMode == ConnectionMode.Local
            && AuthOption == Consts.MCP.Server.AuthOption.token
                ? HttpCredentialMode.AccessToken
                : HttpCredentialMode.Oauth;

        /// <summary>Convenience: <see cref="OperatingSystem"/> is <see cref="OperatingSystemKind.Windows"/>.</summary>
        public bool IsWindows => OperatingSystem == OperatingSystemKind.Windows;

        // --- mcp-authorize b6: project-pinned, marker-aware identity resolution (design 03/06). ---
        // Every config writer consults the same ProjectIdentity + project marker so the routing pin
        // and the per-project local port can never silently diverge between the plugin and a
        // terminal-written config. Resolved lazily and cached (a marker read is file I/O, and the
        // identity is stable for the settings' <see cref="ProjectRootPath"/>).
        private ProjectIdentity? _identity;

        /// <summary>
        /// The project's <see cref="ProjectIdentity"/> — routing pin plus the resolved local port
        /// (the project marker's <c>portOverride</c> wins, else the deterministic hash-derived port).
        /// Read once from the marker at <see cref="ProjectRootPath"/> and cached.
        /// </summary>
        public ProjectIdentity Identity => _identity ??= ProjectIdentity.Derive(ProjectRootPath, ProjectMarker.Read(ProjectRootPath));

        /// <summary>
        /// The routing pin written into every config (HTTP <c>/p/&lt;pin&gt;</c> segment and stdio
        /// <c>project=&lt;pin&gt;</c> arg). This is the <b>v2</b> pin (first 8 hex chars of the
        /// separator-normalized SHA-256, see <see cref="ProjectIdentity.DerivePinV2"/>) so a config
        /// generated from a Windows backslash root matches a plugin whose forward-slash hash it now
        /// equals (auth-fixes T3 / defect B5). Old (v1-pin) configs still route via the plugin's legacy
        /// hash. Non-secret, safe to commit. The port stays on the v1 derivation (<see cref="ResolvedPort"/>)
        /// until the engine runtimes adopt the v2 port primitive.
        /// </summary>
        public string ProjectPin => ProjectIdentity.DerivePinV2(ProjectRootPath);

        /// <summary>
        /// The resolved per-project local port: the project marker's <c>portOverride</c> when set,
        /// otherwise the deterministic hash-derived port. This is the port written into configs
        /// (stdio <c>port=</c> arg and the loopback HTTP URL), NOT the raw engine-supplied
        /// <see cref="Port"/> — b6 single-sources the local port from <see cref="ProjectIdentity"/>.
        /// </summary>
        public int ResolvedPort => Identity.Port;

        /// <summary>
        /// The HTTP <c>url</c> written into configs on the default (credential-free) path: the base
        /// <see cref="Host"/> with the <c>/p/&lt;pin&gt;</c> routing path segment appended, and — for a
        /// loopback URL in <see cref="ConnectionMode.Local"/> — the port rewritten to
        /// <see cref="ResolvedPort"/>. The pin rides as a path segment (not a query param) so a lost
        /// pin 404s loudly instead of silently degrading to unpinned routing (design 03 Flow F).
        /// </summary>
        public string PinnedHttpUrl => BuildPinnedHttpUrl();

        private string BuildPinnedHttpUrl()
        {
            var pin = ProjectPin;
            if (Uri.TryCreate(Host, UriKind.Absolute, out var uri))
            {
                // Only the per-project LOCAL loopback port is rewritten from ProjectIdentity; a hosted
                // (or non-loopback) target keeps its authority verbatim (default ports stay implicit).
                var authority = ConnectionMode == ConnectionMode.Local && uri.IsLoopback
                    ? $"{uri.Scheme}://{uri.Host}:{ResolvedPort}"
                    : uri.GetLeftPart(UriPartial.Authority);
                var basePath = uri.AbsolutePath.TrimEnd('/');
                return $"{authority}{basePath}/p/{pin}{uri.Query}";
            }
            // Non-absolute host (defensive): append the pin segment without a port rewrite.
            return $"{Host.TrimEnd('/')}/p/{pin}";
        }
    }
}
