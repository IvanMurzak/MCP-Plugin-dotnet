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
using System.Globalization;
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
    /// Which <see cref="ProjectIdentity"/> derivation backs the deterministic LOCAL port the config
    /// writers emit (the stdio <c>port=</c> arg and the loopback HTTP url). This is the engine-scoped
    /// policy seam design 02 § D21 (OQ3) codifies: the routing <b>pin</b> stays v2 for EVERY engine
    /// regardless of this value — only the hash-derived port fallback differs.
    ///
    /// <list type="bullet">
    ///   <item><see cref="V2"/> (default) — the separator-normalized derivation Unity and Godot bind.</item>
    ///   <item><see cref="V1"/> — the legacy derivation the <b>Unreal</b> .NET sidecar binds (its
    ///   <c>ProjectConnectionResolver</c> resolves the local port via v1 <see cref="ProjectIdentity.Derive"/>).
    ///   Pinned for the Unreal local plane by owner ruling D21 so the written port matches what the sidecar
    ///   actually binds; a later migration may realign it to <see cref="V2"/>.</item>
    /// </list>
    /// </summary>
    public enum LocalPortDerivation
    {
        /// <summary>v2 separator-normalized derivation (<see cref="ProjectIdentity.DeriveV2"/>). Unity + Godot. The default.</summary>
        V2,

        /// <summary>v1 legacy derivation (<see cref="ProjectIdentity.Derive"/>). The Unreal local plane (design 02 § D21).</summary>
        V1
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

        /// <summary>
        /// The raw engine-supplied port (the port the engine's own binder resolved). NOT what the
        /// config writers emit: both the stdio <c>port=</c> arg and the loopback HTTP url use
        /// <see cref="PinnedPort"/>. This value backs the Docker container name / port mapping and
        /// the displayed copy-paste <c>mcp add</c> one-liners.
        /// </summary>
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

        /// <summary>
        /// The <see cref="ProjectIdentity"/> derivation used for the deterministic LOCAL port the config
        /// writers emit. Defaults to <see cref="LocalPortDerivation.V2"/> (Unity / Godot); the Unreal
        /// sidecar consumer passes <see cref="LocalPortDerivation.V1"/> so the written port matches the v1
        /// port its bridge binds (design 02 § D21). Never affects the routing <see cref="ProjectPin"/>,
        /// which stays v2 for every engine.
        /// </summary>
        public LocalPortDerivation LocalPortDerivation { get; }

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
            string dockerImage = "aigamedeveloper/mcp-server",
            LocalPortDerivation localPortDerivation = LocalPortDerivation.V2)
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
            LocalPortDerivation = localPortDerivation;
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
            string dockerImage = "aigamedeveloper/mcp-server",
            LocalPortDerivation localPortDerivation = LocalPortDerivation.V2)
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
                dockerImage: dockerImage,
                localPortDerivation: localPortDerivation);
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
        private ProjectIdentity? _portIdentity;
        private ProjectMarker? _marker;
        private bool _markerRead;

        /// <summary>
        /// The project marker at <see cref="ProjectRootPath"/>, read once (a marker read is file I/O) and
        /// cached — <c>null</c> when the project has no marker. The pin identity and the engine-aware port
        /// identity both read off this single result, so an override or target is read from disk exactly
        /// once. The <see cref="_markerRead"/> flag distinguishes "not read yet" from "read, was null".
        /// </summary>
        private ProjectMarker? Marker
        {
            get
            {
                if (!_markerRead)
                {
                    _marker = ProjectMarker.Read(ProjectRootPath);
                    _markerRead = true;
                }
                return _marker;
            }
        }

        /// <summary>
        /// The project's <b>v2</b> <see cref="ProjectIdentity"/> — the source of the routing
        /// <see cref="ProjectPin"/> (the registration hash stays v2 for EVERY engine, design 02 § D21) and,
        /// on the default <see cref="LocalPortDerivation.V2"/> path, of the emitted port too. Read once from
        /// the marker at <see cref="ProjectRootPath"/> and cached.
        ///
        /// <para>Derived via <see cref="ProjectIdentity.DeriveV2"/>, so <see cref="ProjectPin"/> and — on
        /// the v2 path — <see cref="ResolvedPort"/> come from ONE <see cref="ProjectIdentity.NormalizeV2"/>
        /// pre-hash string (auth-fixes T1 / defect B). The single sanctioned exception is an engine on
        /// <see cref="LocalPortDerivation.V1"/> (Unreal): its port comes from a separate v1
        /// <see cref="PortIdentity"/> while the pin stays on this v2 object — the controlled v2-pin /
        /// v1-port split D21 authorizes so the written port matches the Unreal v1 sidecar binder.</para>
        /// </summary>
        public ProjectIdentity Identity => _identity ??= ProjectIdentity.DeriveV2(ProjectRootPath, Marker);

        /// <summary>
        /// The <see cref="ProjectIdentity"/> whose hash-derived <see cref="ProjectIdentity.Port"/> backs
        /// <see cref="ResolvedPort"/>. Engine-aware per <see cref="LocalPortDerivation"/> (design 02 § D21):
        /// <see cref="LocalPortDerivation.V1"/> (Unreal) derives via the legacy v1
        /// <see cref="ProjectIdentity.Derive"/> to match its sidecar binder; every other engine REUSES the
        /// v2 <see cref="Identity"/> object, so the default path is byte-identical to before. The marker
        /// <c>portOverride</c> wins on both derivations (it is not hash-dependent), so
        /// <see cref="PinnedPort"/>'s three-level precedence is unchanged — only the derived fallback
        /// (level 3) switches derivation version.
        /// </summary>
        private ProjectIdentity PortIdentity => _portIdentity ??=
            LocalPortDerivation == LocalPortDerivation.V1
                ? ProjectIdentity.Derive(ProjectRootPath, Marker)
                : Identity;

        /// <summary>
        /// The routing pin written into every config (HTTP <c>/p/&lt;pin&gt;</c> segment and stdio
        /// <c>project=&lt;pin&gt;</c> arg). This is the <b>v2</b> pin (first 8 hex chars of the
        /// separator-normalized SHA-256, see <see cref="ProjectIdentity.DerivePinV2"/>) so a config
        /// generated from a Windows backslash root matches a plugin whose forward-slash hash it now
        /// equals (auth-fixes T3 / defect B5). Old (v1-pin) configs still route via the plugin's legacy
        /// hash. Non-secret, safe to commit. Read straight off <see cref="Identity"/> — see
        /// <see cref="ProjectIdentity.DeriveV2"/> for why the pin and the port must share one derivation.
        /// </summary>
        public string ProjectPin => Identity.Pin;

        /// <summary>
        /// The marker-resolved per-project local port: the project marker's <c>portOverride</c> when
        /// set, otherwise the deterministic hash-derived port — b6 single-sources the local port
        /// from <see cref="ProjectIdentity"/> rather than the raw engine-supplied <see cref="Port"/>. The
        /// derivation is engine-aware (<see cref="LocalPortDerivation"/>): v2 by default (Unity / Godot),
        /// v1 for the Unreal local plane (design 02 § D21) so the emitted port matches the v1 port the
        /// Unreal sidecar binds. The routing <see cref="ProjectPin"/> stays v2 regardless.
        ///
        /// <para>This is NOT what the config writers emit. Both transports write
        /// <see cref="PinnedPort"/>, which layers a port the user typed into <see cref="Host"/>
        /// BETWEEN this property's two cases (defect A). <see cref="ResolvedPort"/> therefore supplies
        /// precedence levels 1 and 3; see <see cref="PinnedPort"/> for the full ordering.</para>
        ///
        /// <para>Reads off <see cref="PortIdentity"/> (a v1 identity for Unreal, else the shared v2
        /// <see cref="Identity"/>). On the default v2 path it and <see cref="ProjectPin"/> still share the
        /// exact <see cref="ProjectIdentity.NormalizeV2"/> pre-hash string (auth-fixes T1 / defect B; see
        /// <see cref="ProjectIdentity.DeriveV2"/>); on the Unreal v1 path only the port switches derivation,
        /// the pin stays v2.</para>
        /// </summary>
        public int ResolvedPort => PortIdentity.Port;

        /// <summary>
        /// The port the user explicitly typed into <see cref="Host"/>, or <c>null</c> when
        /// <see cref="Host"/> carries no explicit port. See <see cref="TryGetExplicitPort"/> for why
        /// this is read off the RAW host string rather than <c>Uri.Port</c>.
        /// </summary>
        public int? ExplicitHostPort => TryGetExplicitPort(Host);

        /// <summary>
        /// The per-project local port the config writers emit — the stdio <c>port=</c> arg AND the
        /// loopback HTTP <c>url</c> (<see cref="PinnedHttpUrl"/>) — resolved by a three-level precedence
        /// (auth-fixes T1 / defect A, owner ruling 2026-07-19, extended to stdio 2026-07-19):
        /// <list type="number">
        ///   <item>the project marker's <c>portOverride</c> — an explicit per-project pin, wins outright;</item>
        ///   <item>an explicit port in the <see cref="Host"/> URL — the port the USER typed;</item>
        ///   <item>the deterministic hash-derived port (<see cref="ResolvedPort"/>) — the fallback
        ///         when <see cref="Host"/> carries no explicit port; v2 by default, v1 for the Unreal
        ///         local plane (<see cref="LocalPortDerivation"/>, design 02 § D21).</item>
        /// </list>
        ///
        /// <para>Level 2 exists because the engine runtime ALREADY binds the typed port: Unity's
        /// <c>UnityMcpPluginEditor.Port</c> returns <c>uri.Port</c> whenever <c>Host</c> parses as an
        /// absolute URI with an in-range port, and only falls back to the derived port otherwise. Before
        /// this fix the writer overwrote that typed port with the derived one, so the server listened on
        /// the port the user chose while the config told the agent to dial a different one. Honouring the
        /// typed port makes the WRITER agree with the BINDER — that agreement is the whole point.</para>
        ///
        /// <para><b>Why this is transport-neutral</b> (it was named <c>PinnedHttpPort</c> when only the
        /// HTTP url consumed it): Unity's binder property is not transport-scoped — it resolves the ONE
        /// port the plugin binds, and the stdio server the config spawns dials that same port. So a stdio
        /// <c>port=</c> arg on the old two-level precedence disagreed with the binder in exactly the way
        /// the HTTP url used to. The per-transport difference lives at the CALL SITE, not here:
        /// <see cref="BuildPinnedHttpUrl"/> applies this port only to a <see cref="ConnectionMode.Local"/>
        /// loopback authority (a hosted target keeps its authority verbatim, which already preserved any
        /// typed port), whereas stdio has no authority to preserve and applies the precedence directly.
        /// Both end up naming the port the binder actually binds.</para>
        ///
        /// <para>Level 1 still outranks level 2: <c>portOverride</c> is a deliberate per-project marker
        /// written to pin a project's port, so it beats an incidental port in the host string.</para>
        ///
        /// <para><b>Residual divergence from the binder</b> (both engine-side, both outside this writer's
        /// reach — recorded so the agreement above is not read as absolute):
        /// <list type="bullet">
        ///   <item>A <see cref="Host"/> with NO port (or an empty <c>host:</c>) — the binder's
        ///   <c>uri.Port</c> synthesises the scheme default (80/443), which passes its
        ///   <c>&gt; 0 &amp;&amp; &lt;= MaxPort</c> guard, so it binds 80 while this writer falls back to the
        ///   derived port. Unreachable on the shipped default, whose <c>Host</c> is
        ///   <c>http://localhost:{derived}</c> — an explicit port that both sides agree on.</item>
        ///   <item>Level 1 vs level 2 — the binder never reads the project marker, so a
        ///   <c>portOverride</c> combined with an explicit port in <see cref="Host"/> resolves to the
        ///   override here and to the typed port there. Latent: nothing writes <c>portOverride</c> in
        ///   production today.</item>
        ///   <item><b>Level 2 is a UNITY-shaped rule.</b> The claim above that the binder honours a typed
        ///   port holds for Unity's binder; the other two engine consumers deliberately do the OPPOSITE
        ///   on loopback. Godot's <c>GodotProjectIdentity.ResolveLocalServerBindPort</c> ignores a
        ///   loopback host's own explicit port and binds the derived port ("a loopback port override goes
        ///   through the marker <c>portOverride</c>"), and Unreal's bridge asserts the written config port
        ///   is never the raw engine-supplied host port. So on a <see cref="ConnectionMode.Local"/>
        ///   loopback <see cref="Host"/> carrying a typed port, level 2 makes this writer disagree with
        ///   BOTH of those binders — the inverse of the Unity case it was introduced for. Each pins the
        ///   old invariant in a test that fails on their next McpPlugin bump
        ///   (<c>ResolveLocalServerBindPort_LoopbackExplicitPort_StillDerives_MatchingTheWriter</c>,
        ///   <c>WrittenConfigPort_EqualsServerBindPort_OnDefaultLocalPath</c>). D21 has since codified the
        ///   per-engine policy seam HERE for a DIFFERENT axis — the level-3 derivation VERSION, v1 for the
        ///   Unreal local plane via <see cref="LocalPortDerivation"/> — so the writer's derived fallback now
        ///   matches the Unreal v1 binder. That is orthogonal to the level-2 typed-host divergence this
        ///   bullet describes, whose cross-engine reconciliation stays an OWNER call out of scope for this
        ///   writer-side change.</item>
        /// </list></para>
        /// </summary>
        public int PinnedPort =>
            // marker override (1) else typed host port (2) else derived port (3, v2 or v1 for Unreal) — see
            // the list above. The override flag reads off PortIdentity so it agrees with ResolvedPort on the
            // v1 (Unreal) path; on the v2 default PortIdentity IS Identity, so this is byte-identical.
            PortIdentity.PortIsOverridden ? ResolvedPort : ExplicitHostPort ?? ResolvedPort;

        /// <summary>
        /// The HTTP <c>url</c> written into configs on the default (credential-free) path: the base
        /// <see cref="Host"/> with the <c>/p/&lt;pin&gt;</c> routing path segment appended, and — for a
        /// loopback URL in <see cref="ConnectionMode.Local"/> — the port set to
        /// <see cref="PinnedPort"/>. The pin rides as a path segment (not a query param) so a lost
        /// pin 404s loudly instead of silently degrading to unpinned routing (design 03 Flow F).
        /// </summary>
        public string PinnedHttpUrl => BuildPinnedHttpUrl();

        private string BuildPinnedHttpUrl()
        {
            var pin = ProjectPin;
            if (Uri.TryCreate(Host, UriKind.Absolute, out var uri))
            {
                // Only the per-project LOCAL loopback port is resolved here; a hosted (or non-loopback)
                // target keeps its authority verbatim (default ports stay implicit). The loopback port
                // follows PinnedPort's marker > typed > derived precedence, so a port the user typed
                // into Host survives into the written config instead of being silently overwritten.
                var authority = ConnectionMode == ConnectionMode.Local && uri.IsLoopback
                    ? $"{uri.Scheme}://{uri.Host}:{PinnedPort}"
                    : uri.GetLeftPart(UriPartial.Authority);
                var basePath = uri.AbsolutePath.TrimEnd('/');
                return $"{authority}{basePath}/p/{pin}{uri.Query}";
            }
            // Non-absolute host (defensive): append the pin segment without a port rewrite.
            return $"{Host.TrimEnd('/')}/p/{pin}";
        }

        /// <summary>
        /// Reads an explicitly-typed port out of a host string, or returns <c>null</c> when there is
        /// none. Returns <c>null</c> for an out-of-range port too, mirroring the engine binder's
        /// <c>uri.Port &gt; 0 &amp;&amp; uri.Port &lt;= Consts.Hub.MaxPort</c> guard — an unusable port
        /// falls back to the derived one on BOTH sides rather than diverging.
        ///
        /// <para><b>Why the raw string and not <c>Uri.Port</c>:</b> <see cref="Uri"/> synthesises the
        /// scheme's default port, so <c>http://localhost/mcp</c> and <c>http://localhost:80/mcp</c> both
        /// report <c>80</c> and become indistinguishable. The first has no user intent behind it and must
        /// fall through to the derived port; the second is a port the user deliberately typed and must be
        /// honoured. Only the raw authority preserves that distinction.</para>
        /// </summary>
        internal static int? TryGetExplicitPort(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            // Isolate the authority: after "scheme://" (if present), up to the first '/', '?' or '#'.
            var schemeEnd = host!.IndexOf("://", StringComparison.Ordinal);
            var start = schemeEnd >= 0 ? schemeEnd + 3 : 0;
            var end = host.IndexOfAny(AuthorityTerminators, start);
            var authority = end >= 0 ? host.Substring(start, end - start) : host.Substring(start);

            // Drop any userinfo ("user:pass@host:port") — its colon is not a port separator.
            var at = authority.LastIndexOf('@');
            if (at >= 0)
                authority = authority.Substring(at + 1);

            // For an IPv6 literal the port follows the closing bracket ("[::1]:8080"); the colons inside
            // the brackets are part of the address. LastIndexOf returns -1 for a normal host, so the
            // search then starts at 0 and finds a plain "host:port" colon.
            var colon = authority.IndexOf(':', authority.LastIndexOf(']') + 1);
            if (colon < 0 || colon == authority.Length - 1)
                return null;

            // NumberStyles.None is the whole validator: it rejects sign, whitespace, separators and any
            // non-ASCII-digit character, so a hand-rolled digit scan on top would be redundant. The
            // theory in ConfigWritersB6Tests locks that (non-numeric, non-ASCII-digit and overflow rows).
            if (!int.TryParse(authority.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
                return null;

            return port > 0 && port <= Consts.Hub.MaxPort ? port : (int?)null;
        }

        /// <summary>Characters that terminate a URL authority. Static so the parse allocates nothing.</summary>
        private static readonly char[] AuthorityTerminators = { '/', '?', '#' };
    }
}
