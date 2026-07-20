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
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Consts.MCP.Server.TransportMethod;

    /// <summary>
    /// mcp-authorize b6 — config writers flipped to the credential-free, project-pinned shape
    /// (design 03/06). Covers the four Definition-of-Done items: (1) per-configurator snapshot of
    /// the new shapes for all 16 configurators + dedup/upsert regression; (2) marker-override
    /// propagation into the written port; (3) the security assertion that NO credential can reach a
    /// project-scoped config file on the default path; plus the <c>SupportsOAuth</c> flag and the
    /// advanced-PAT escape hatch (explicit opt-in, env-var/user-scope preference, project-file warning).
    /// </summary>
    public class ConfigWritersB6Tests
    {
        // A token value distinctive enough that a substring assertion cannot false-match.
        private const string PatValue = "S3CR3T-PAT-VALUE";

        // A literal backslash, built from its char code so no escaping subtlety can alter it.
        private static string Bs => ((char)92).ToString();

        private static AgentConfiguratorSettings Settings(
            string root,
            string? token = null,
            ConnectionMode connectionMode = ConnectionMode.Local,
            Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none,
            string host = "http://localhost:50000/mcp") => new(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: root,
                executableFullPath: "C:/Tools/ai-game-developer-mcp-server.exe",
                port: 50000,
                timeoutMs: 30000,
                host: host,
                token: token,
                connectionMode: connectionMode,
                authOption: authOption);

        private static string NewTempDir()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        private static IEnumerable<AiAgentConfigurator> RealConfigurators()
            => AiAgentConfiguratorRegistry.All.Where(c => c is not CustomConfigurator);

        private static bool IsUnderRoot(string configPath, string root)
        {
            static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
            return Norm(configPath).StartsWith(Norm(root) + "/", StringComparison.OrdinalIgnoreCase);
        }

        // ---- DoD 1: SupportsOAuth flag (default true across the registry). ----

        [Fact]
        public void AllConfigurators_DefaultToSupportsOAuth()
        {
            foreach (var c in AiAgentConfiguratorRegistry.All)
                c.SupportsOAuth.ShouldBeTrue($"{c.AgentId} should default SupportsOAuth=true");
        }

        // ---- DoD 1: per-configurator snapshot of the new default (OAuth) shapes — credential-free + pinned. ----

        [Fact]
        public void AllConfigurators_DefaultHttp_AreCredentialFree_AndProjectPinned()
        {
            var root = NewTempDir();
            try
            {
                // A token + authorization=required is the pre-b6 "would have injected a Bearer header"
                // condition; the default path must strip it regardless.
                var settings = Settings(root, token: PatValue, authOption: Consts.MCP.Server.AuthOption.required);
                foreach (var c in RealConfigurators())
                {
                    var content = c.GetHttpConfig(settings).ExpectedFileContent;

                    content.ShouldContain($"/p/{settings.ProjectPin}", customMessage: $"{c.AgentId} HTTP must carry the pin path segment");
                    content.ShouldNotContain(PatValue, customMessage: $"{c.AgentId} HTTP must not embed the token value");
                    content.ShouldNotContain("Bearer", customMessage: $"{c.AgentId} HTTP must not embed a Bearer header");
                    content.ShouldNotContain("bearer_token_env_var", customMessage: $"{c.AgentId} HTTP must not carry the PAT env-var indirection on the default path");
                }
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void AllConfigurators_DefaultStdio_AreCredentialFree_AndProjectPinned()
        {
            var root = NewTempDir();
            try
            {
                var settings = Settings(root, token: PatValue, authOption: Consts.MCP.Server.AuthOption.required);
                foreach (var c in RealConfigurators())
                {
                    var content = c.GetStdioConfig(settings).ExpectedFileContent;

                    content.ShouldContain($"{Consts.MCP.Server.Args.Project}={settings.ProjectPin}", customMessage: $"{c.AgentId} stdio must carry project=<pin>");
                    content.ShouldContain($"{Consts.MCP.Server.Args.Port}={settings.ResolvedPort}", customMessage: $"{c.AgentId} stdio must carry the ProjectIdentity port");
                    content.ShouldNotContain($"{Consts.MCP.Server.Args.Authorization}=", customMessage: $"{c.AgentId} stdio must not carry an authorization arg on the default path");
                    content.ShouldNotContain($"{Consts.MCP.Server.Args.Token}=", customMessage: $"{c.AgentId} stdio must not carry a token arg on the default path");
                    content.ShouldNotContain(PatValue, customMessage: $"{c.AgentId} stdio must not embed the token value");
                }
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Custom_DefaultHttpSnippet_IsCredentialFree_AndProjectPinned()
        {
            var root = NewTempDir();
            try
            {
                var c = new CustomConfigurator();
                var settings = Settings(root, token: PatValue, authOption: Consts.MCP.Server.AuthOption.required);
                var snippet = c.Describe(settings, TransportMethod.streamableHttp).Sections
                    .SelectMany(s => s.Items)
                    .Select(i => i.Text ?? string.Empty)
                    .First(t => t.Contains("mcpServers"));

                snippet.ShouldContain($"/p/{settings.ProjectPin}");
                snippet.ShouldNotContain(PatValue);
                snippet.ShouldNotContain("Bearer");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void ClaudeCode_NewShapes_ExactContent()
        {
            var root = NewTempDir();
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Settings(root); // host "http://localhost:50000/mcp" — an EXPLICIT typed port
                var pin = settings.ProjectPin;

                // stdio still carries the ProjectIdentity port (marker override, else derived v2).
                var stdio = c.GetStdioConfig(settings).ExpectedFileContent;
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Port}={settings.ResolvedPort}\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.PluginTimeout}=30000\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Project}={pin}\"");
                stdio.ShouldNotContain("\"type\"");
                stdio.ShouldNotContain("\"url\"");

                // auth-fixes T1 / defect A (owner ruling 2026-07-19): the HTTP url keeps the port the
                // user typed into Host (50000) instead of overwriting it with the derived port. The
                // engine binds that same typed port (UnityMcpPluginEditor.Port -> uri.Port), so writer
                // and binder now agree. This assertion previously named settings.ResolvedPort.
                var http = c.GetHttpConfig(settings).ExpectedFileContent;
                http.ShouldContain($"\"url\": \"http://localhost:50000/mcp/p/{pin}\"");
                http.ShouldContain("\"type\": \"http\"");
                http.ShouldNotContain("\"headers\"");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void ClaudeCode_ManualHttpCommand_UsesTheSamePinnedUrl_AsConfigure()
        {
            // B8 (auth-fixes): the displayed "manual" `claude mcp add` command must use the SAME pinned
            // URL that Configure writes into .mcp.json (settings.PinnedHttpUrl, i.e. .../mcp/p/<pin>),
            // not the bare unpinned host — an unpinned URL only routes when the account has one instance.
            var root = NewTempDir();
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Settings(root);
                var pinnedUrl = settings.PinnedHttpUrl;

                // What Configure writes.
                c.GetHttpConfig(settings).ExpectedFileContent.ShouldContain($"\"url\": \"{pinnedUrl}\"");

                // The manual command shown in the UI.
                var manualCommand = c.Describe(settings, TransportMethod.streamableHttp).Sections
                    .SelectMany(s => s.Items)
                    .Select(i => i.Text ?? string.Empty)
                    .First(t => t.Contains("claude mcp add"));

                manualCommand.ShouldContain(pinnedUrl);
                manualCommand.ShouldContain("/p/" + settings.ProjectPin);
                // Never the bare unpinned host (which lacks the /p/<pin> routing segment).
                manualCommand.ShouldNotContain($"http {AiAgentConfig.DefaultMcpServerName} {settings.Host} ");
                manualCommand.Trim().ShouldNotEndWith(settings.Host);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void GlobalConfigurators_AreExplicitlyProjectPinned()
        {
            // Design 06 D14: the three inherently-global configurators still carry the project pin,
            // so even a globally-configured client talks to exactly one chosen project.
            var root = NewTempDir();
            try
            {
                var settings = Settings(root);
                foreach (var id in new[] { "claude-desktop", "antigravity", "cline" })
                {
                    var c = AiAgentConfiguratorRegistry.GetByAgentId(id)!;
                    c.GetHttpConfig(settings).ExpectedFileContent.ShouldContain($"/p/{settings.ProjectPin}", customMessage: $"{id} HTTP must be pinned");
                    c.GetStdioConfig(settings).ExpectedFileContent.ShouldContain($"{Consts.MCP.Server.Args.Project}={settings.ProjectPin}", customMessage: $"{id} stdio must be pinned");
                }
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---- auth-fixes T3 / defect B5: the emitted pin is the v2 (separator-normalized) pin. ----

        [Fact]
        public void ProjectPin_IsV2Pin_NotV1_ForBackslashRoot_AndCarriesIntoConfigs()
        {
            // The configurator MUST emit the v2 pin so a Windows backslash root and its forward-slash
            // form route together (B5). A hardcoded backslash literal keeps this deterministic on Linux
            // CI, where Path.GetTempPath() is forward-slash (there v1 == v2 and the divergence hides).
            var backslashRoot = "C:" + ((char)92) + "Games" + ((char)92) + "MyProj";
            var settings = new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: backslashRoot,
                executableFullPath: "C:/Tools/server.exe",
                port: 50000,
                timeoutMs: 30000,
                host: "https://ai-game.dev/mcp",
                connectionMode: ConnectionMode.Cloud);

            var v2Pin = ProjectIdentity.DerivePinV2(backslashRoot);
            settings.ProjectPin.ShouldBe(v2Pin);
            settings.ProjectPin.ShouldNotBe(ProjectIdentity.DerivePin(backslashRoot)); // v2 ≠ v1 on Windows

            var c = new ClaudeCodeConfigurator();
            c.GetHttpConfig(settings).ExpectedFileContent.ShouldContain($"/p/{v2Pin}");
            c.GetStdioConfig(settings).ExpectedFileContent.ShouldContain($"{Consts.MCP.Server.Args.Project}={v2Pin}");
        }

        // ---- auth-fixes T1 / defect B: the pin and the port come from ONE normalization. ----

        /// <summary>
        /// THE regression test for defect B. Before the fix, one settings object derived its pin from
        /// <c>DerivePinV2</c> (separator-normalized) but its port from the v1 <c>ProjectIdentity.Derive</c>
        /// (NOT normalized), so on a Windows backslash root the written config mixed a v1 port with a v2
        /// pin — e.g. <c>http://localhost:29540/p/a8087ea6</c>, where 29540 is the v1 port of
        /// <c>c:\tmp\mcpauth-test\test-project</c> and a8087ea6 is the v2 pin of
        /// <c>c:/tmp/mcpauth-test/test-project</c>. The engine runtimes bind the v2 port (defect B10), so
        /// the agent dialled a port nothing was listening on.
        ///
        /// <para>A hardcoded backslash literal keeps this deterministic on Linux CI, where
        /// <c>Path.GetTempPath()</c> is forward-slash and v1 == v2 — the exact blind spot that let the
        /// bug ship. The root is a HASH INPUT only; it is never created on disk.</para>
        /// </summary>
        [Fact]
        public void PinAndPort_ComeFromTheSameNormalization_ForBackslashRoot()
        {
            var backslashRoot = "C:" + Bs + "tmp" + Bs + "mcpauth-test" + Bs + "test-project";
            var forwardSlashRoot = backslashRoot.Replace(Bs, "/");
            var settings = Settings(backslashRoot);

            // Identity resolution reads <root>/.ai-game-dev/project.json, so pin this test to the
            // hash-derived branch explicitly: if that path ever gains a real marker with a
            // portOverride, this fails HERE rather than as a baffling port mismatch below.
            settings.Identity.PortIsOverridden.ShouldBeFalse();

            // Both halves of the identity agree with the v2 primitives...
            settings.ProjectPin.ShouldBe(ProjectIdentity.DerivePinV2(backslashRoot));
            settings.ResolvedPort.ShouldBe(ProjectIdentity.DerivePortV2(backslashRoot));

            // ...and, the defining property, with the SAME (forward-slash) pre-hash string, so the
            // separator form the engine happens to report can never split pin from port.
            settings.ProjectPin.ShouldBe(ProjectIdentity.DerivePinV2(forwardSlashRoot));
            settings.ResolvedPort.ShouldBe(ProjectIdentity.DerivePortV2(forwardSlashRoot));

            // The bug itself: the port must NOT be the v1 (un-normalized) derivation, which on a
            // backslash root is a different hash of a different string.
            ProjectIdentity.DerivePortV2(backslashRoot).ShouldNotBe(ProjectIdentity.DerivePort(backslashRoot));
            settings.ResolvedPort.ShouldNotBe(ProjectIdentity.DerivePort(backslashRoot));
        }

        /// <summary>
        /// The same invariant observed end-to-end, through what Configure actually writes: the port in
        /// the URL authority and the pin in the <c>/p/&lt;pin&gt;</c> segment of one written config must
        /// both come from the v2 normalization. This is the user-visible shape from the bug report.
        ///
        /// <para>The host is deliberately PORTLESS so the URL exercises precedence level 3 (the derived
        /// v2 port). With an explicit port in the host the URL would — correctly, per defect A's owner
        /// ruling — carry that typed port instead, and this test would no longer be observing defect B.
        /// The stdio half is unaffected by the host either way.</para>
        /// </summary>
        [Fact]
        public void WrittenConfig_UrlPortAndPin_AreBothV2_ForBackslashRoot()
        {
            var backslashRoot = "C:" + Bs + "tmp" + Bs + "mcpauth-test" + Bs + "test-project";
            var v2Pin = ProjectIdentity.DerivePinV2(backslashRoot);
            var v2Port = ProjectIdentity.DerivePortV2(backslashRoot);
            var settings = Settings(backslashRoot, host: "http://localhost/mcp");

            // Same ambient-state pin as the test above: the derived-port branch, not a marker override.
            settings.Identity.PortIsOverridden.ShouldBeFalse();

            var c = new ClaudeCodeConfigurator();

            var http = c.GetHttpConfig(settings).ExpectedFileContent;
            http.ShouldContain($"\"url\": \"http://localhost:{v2Port}/mcp/p/{v2Pin}\"");
            http.ShouldNotContain($"localhost:{ProjectIdentity.DerivePort(backslashRoot)}");

            var stdio = c.GetStdioConfig(settings).ExpectedFileContent;
            stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Port}={v2Port}\"");
            stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Project}={v2Pin}\"");
        }

        // ---- DoD 1: dedup / upsert regression on the new pinned shape. ----

        [Fact]
        public void Configure_IsIdempotentUpsert_KeepsSingleEntry()
        {
            var root = NewTempDir();
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Settings(root);

                c.GetHttpConfig(settings).Configure().ShouldBeTrue();
                c.GetHttpConfig(settings).Configure().ShouldBeTrue(); // re-configure = upsert
                c.IsConfigured(settings, TransportMethod.streamableHttp).ShouldBeTrue();

                var servers = ReadServers(c.GetHttpConfig(settings).ConfigPath, "mcpServers");
                servers.Count.ShouldBe(1);
                servers.Single().Key.ShouldBe(AiAgentConfig.DefaultMcpServerName);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void Configure_DedupsSameServerUnderADifferentName()
        {
            var root = NewTempDir();
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Settings(root);
                var pinnedUrl = settings.PinnedHttpUrl;
                var configPath = Path.Combine(root, ".mcp.json");

                // Pre-existing entry under a DIFFERENT name but the same (identity) url.
                File.WriteAllText(configPath,
                    $"{{ \"mcpServers\": {{ \"legacy-name\": {{ \"type\": \"http\", \"url\": \"{pinnedUrl}\" }} }} }}");

                c.GetHttpConfig(settings).Configure().ShouldBeTrue();

                var servers = ReadServers(configPath, "mcpServers");
                servers.ContainsKey(AiAgentConfig.DefaultMcpServerName).ShouldBeTrue();
                servers.ContainsKey("legacy-name").ShouldBeFalse();
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---- DoD 2: marker portOverride propagates into the written config port. ----

        /// <summary>
        /// DoD 2, and — since defect A's owner ruling — the ESSENTIAL precedence-level-1-beats-level-2
        /// case: the marker's <c>portOverride</c> (27777) and an explicit port in the host
        /// (<c>http://localhost:50000/mcp</c>) are BOTH present, and the override must win. A marker is
        /// a deliberate per-project pin; a port in the host string is incidental by comparison.
        /// </summary>
        [Fact]
        public void MarkerPortOverride_PropagatesToWrittenPort_StdioAndHttp()
        {
            var root = NewTempDir();
            try
            {
                const int overridePort = 27777;
                new ProjectMarker { PortOverride = overridePort }.Write(root);

                var settings = Settings(root); // host carries an explicit :50000 the override must beat
                settings.ExplicitHostPort.ShouldBe(50000);
                settings.ResolvedPort.ShouldBe(overridePort);
                settings.Identity.PortIsOverridden.ShouldBeTrue();
                settings.PinnedHttpPort.ShouldBe(overridePort);

                var c = new ClaudeCodeConfigurator();
                c.GetStdioConfig(settings).ExpectedFileContent.ShouldContain($"{Consts.MCP.Server.Args.Port}={overridePort}");

                var http = c.GetHttpConfig(settings).ExpectedFileContent;
                http.ShouldContain($"localhost:{overridePort}/");
                http.ShouldNotContain("localhost:50000"); // the typed host port must NOT win over the marker
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---- auth-fixes T1 / defect A (owner ruling 2026-07-19): the user's typed port is honoured. ----
        //
        // Precedence written into the config's loopback HTTP url:
        //   1. project marker portOverride   (highest — MarkerPortOverride_PropagatesToWrittenPort_* above)
        //   2. explicit port in the Host URL (the port the user typed)
        //   3. deterministic derived v2 port (fallback when Host carries no explicit port)
        //
        // Level 2 is the fix: the engine ALREADY binds the typed port (UnityMcpPluginEditor.Port returns
        // uri.Port when Host parses with an in-range port), so overwriting it in the writer made the
        // config point at a port nothing was listening on — the same class of failure as defect B, from
        // the opposite direction.

        /// <summary>
        /// Precedence levels 2 and 3, end-to-end. A <paramref name="typedPort"/> of <c>null</c> means the
        /// host carries no explicit port, so the derived v2 port is expected instead.
        ///
        /// <para>The <c>:80</c> row is the case <c>Uri.Port</c> cannot express: it synthesises 80 for a
        /// PORTLESS host too, so only the raw host string distinguishes "the user typed 80" (honour it)
        /// from "no port at all" (derive one). Level 1 lives in
        /// <c>MarkerPortOverride_PropagatesToWrittenPort_StdioAndHttp</c>, where it beats a typed port.</para>
        /// </summary>
        [Theory]
        [InlineData("http://localhost:50000/mcp", 50000)] // a typed port is honoured, not overwritten
        [InlineData("http://localhost:80/mcp", 80)]       // an explicit scheme-DEFAULT port is still explicit
        [InlineData("http://localhost/mcp", null)]        // no port at all -> derived v2 port
        public void LoopbackHttpUrl_HonoursTypedPort_ElseDerivedV2Port(string host, int? typedPort)
        {
            var root = NewTempDir();
            try
            {
                var settings = Settings(root, host: host);
                var expectedPort = typedPort ?? settings.ResolvedPort;

                settings.Identity.PortIsOverridden.ShouldBeFalse(); // no marker => levels 2/3 only
                settings.ExplicitHostPort.ShouldBe(typedPort);
                settings.PinnedHttpPort.ShouldBe(expectedPort);

                if (typedPort.HasValue)
                    // The derived port is a DIFFERENT value, so the row genuinely distinguishes the paths
                    // (the derived range is 20000-29999, which neither 50000 nor 80 can collide with).
                    settings.ResolvedPort.ShouldNotBe(typedPort.Value);
                else
                    settings.PinnedHttpPort.ShouldBe(ProjectIdentity.DerivePortV2(root));

                settings.PinnedHttpUrl.ShouldBe($"http://localhost:{expectedPort}/mcp/p/{settings.ProjectPin}");

                // ...and it reaches the file the user actually gets.
                new ClaudeCodeConfigurator().GetHttpConfig(settings).ExpectedFileContent
                    .ShouldContain($"\"url\": \"http://localhost:{expectedPort}/mcp/p/{settings.ProjectPin}\"");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void ExplicitHostPort_IsIgnored_ForNonLoopbackAndCloud()
        {
            // The port rewrite only ever applied to a LOCAL loopback authority; a hosted target keeps
            // its authority verbatim, which already preserved any typed port. Pinned so the defect-A
            // change cannot leak into the cloud path.
            var root = NewTempDir();
            try
            {
                var cloud = Settings(root, connectionMode: ConnectionMode.Cloud, host: "https://ai-game.dev/mcp");
                cloud.PinnedHttpUrl.ShouldBe($"https://ai-game.dev/mcp/p/{cloud.ProjectPin}");
                cloud.PinnedHttpUrl.ShouldNotContain($":{cloud.ResolvedPort}");

                var lan = Settings(root, host: "http://192.168.1.5:9000/mcp");
                lan.PinnedHttpUrl.ShouldBe($"http://192.168.1.5:9000/mcp/p/{lan.ProjectPin}");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Theory]
        // No port at all.
        [InlineData("http://localhost/mcp", null)]
        [InlineData("http://localhost", null)]
        // Ordinary explicit ports, including a scheme-default one and the range bounds.
        [InlineData("http://localhost:50000/mcp", 50000)]
        [InlineData("http://localhost:80/mcp", 80)]
        [InlineData("https://example.com:443", 443)]
        [InlineData("http://localhost:1", 1)]
        [InlineData("http://localhost:65535", 65535)]
        // Out of range / unusable => null, mirroring the engine binder's guard, so BOTH sides fall back
        // to the derived port rather than diverging.
        [InlineData("http://localhost:0/mcp", null)]
        [InlineData("http://localhost:65536/mcp", null)]
        [InlineData("http://localhost:99999999999999999999/mcp", null)]
        // The authority ends at the first '/', '?' or '#' — a query/fragment with no path still terminates it.
        [InlineData("http://localhost:8080?x=1", 8080)]
        [InlineData("http://localhost:8080#frag", 8080)]
        // Scheme-relative: no "://", so there is no authority to read and the answer is null. That
        // AGREES with the writer and the binder, which both reject it at Uri.TryCreate(Absolute) and
        // fall back to the derived port.
        [InlineData("//localhost:8080/mcp", null)]
        // Colons that are NOT port separators.
        [InlineData("http://user:pass@localhost/mcp", null)]
        [InlineData("http://user:pass@localhost:8080/mcp", 8080)]
        [InlineData("http://[::1]/mcp", null)]
        [InlineData("http://[::1]:8080/mcp", 8080)]
        // Malformed / empty. The non-ASCII-digit row (Arabic-Indic "34") pins that NumberStyles.None is
        // the only validator needed — it is what lets the parser drop a hand-rolled digit scan.
        [InlineData("http://localhost:/mcp", null)]
        [InlineData("http://localhost:abc/mcp", null)]
        [InlineData("http://localhost:٣٤/mcp", null)]
        [InlineData("http://localhost:8 0/mcp", null)]
        [InlineData("http://localhost:+80/mcp", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void TryGetExplicitPort_ReadsTheTypedPort_FromTheRawHost(string? host, int? expected)
            => AgentConfiguratorSettings.TryGetExplicitPort(host).ShouldBe(expected);

        [Fact]
        public void NoMarker_UsesDeterministicDerivedPort()
        {
            var root = NewTempDir();
            try
            {
                var settings = Settings(root);
                settings.Identity.PortIsOverridden.ShouldBeFalse();
                // auth-fixes T1 / defect B: the derived port is the v2 (separator-normalized) port —
                // the SAME normalization ProjectPin uses. This assertion previously named DerivePort
                // (v1); on a forward-slash root the two agree, which is precisely why the suite could
                // not see the Windows divergence. See PinAndPort_ComeFromTheSameNormalization_* below.
                settings.ResolvedPort.ShouldBe(ProjectIdentity.DerivePortV2(root));
                settings.ResolvedPort.ShouldBeInRange(ProjectIdentity.MinPort, ProjectIdentity.MaxPort);
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---- DoD 3: NO credential can reach a project-scoped config FILE on the default path. ----

        [Fact]
        public void DefaultPath_WritesNoCredentialIntoAnyProjectScopedFile()
        {
            var root = NewTempDir();
            try
            {
                // Strongest condition: a token is present AND auth is "required" — pre-b6 this wrote a
                // credential into the project file for every project-scoped configurator.
                var settings = Settings(root, token: PatValue, authOption: Consts.MCP.Server.AuthOption.required);
                var assertedAtLeastOne = false;

                foreach (var c in RealConfigurators())
                {
                    foreach (var transport in new[] { TransportMethod.stdio, TransportMethod.streamableHttp })
                    {
                        var config = transport == TransportMethod.stdio
                            ? c.GetStdioConfig(settings)
                            : c.GetHttpConfig(settings);

                        // Only touch disk for project-scoped configs (never pollute a global user path).
                        if (!IsUnderRoot(config.ConfigPath, root))
                            continue;

                        config.Configure().ShouldBeTrue($"{c.AgentId}/{transport} should configure");
                        var onDisk = File.ReadAllText(config.ConfigPath);

                        onDisk.ShouldNotContain(PatValue, customMessage: $"{c.AgentId}/{transport} leaked the token value into a project file");
                        onDisk.ShouldNotContain("Bearer", customMessage: $"{c.AgentId}/{transport} leaked a Bearer header into a project file");
                        onDisk.ShouldNotContain($"{Consts.MCP.Server.Args.Token}=", customMessage: $"{c.AgentId}/{transport} leaked a token arg into a project file");
                        assertedAtLeastOne = true;
                    }
                }

                assertedAtLeastOne.ShouldBeTrue("expected at least one project-scoped configurator to be asserted on disk");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // ---- Advanced PAT escape hatch: explicit opt-in, env-var/user-scope preference, project-file warning. ----

        [Fact]
        public void AdvancedPat_WritesBearerHeader_AndWarnsForProjectScopedFile()
        {
            var root = NewTempDir();
            try
            {
                var c = new ClaudeCodeConfigurator(); // project-scoped .mcp.json
                var settings = Settings(root, token: PatValue);
                var logger = new CapturingLogger();

                var config = c.GetHttpConfig(settings, logger, HttpCredentialMode.AccessToken);

                config.ExpectedFileContent.ShouldContain($"Bearer {PatValue}");
                logger.Warnings.ShouldContain(m => m.Contains("project-scoped"));
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void AdvancedPat_GlobalConfig_WritesHeader_ButDoesNotWarn()
        {
            var root = NewTempDir();
            try
            {
                var c = new ClaudeDesktopConfigurator(); // global per-OS path
                var settings = Settings(root, token: PatValue);
                var logger = new CapturingLogger();

                var config = c.GetHttpConfig(settings, logger, HttpCredentialMode.AccessToken);

                config.ExpectedFileContent.ShouldContain($"Bearer {PatValue}");
                logger.Warnings.ShouldBeEmpty(); // user-scope placement — the preferred PAT location.
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void AdvancedPat_Codex_UsesEnvVar_KeepsTokenOutOfFile_NoWarning()
        {
            var root = NewTempDir();
            try
            {
                var c = new CodexConfigurator(); // project-scoped .codex/config.toml
                var settings = Settings(root, token: PatValue);
                var logger = new CapturingLogger();

                var config = c.GetHttpConfig(settings, logger, HttpCredentialMode.AccessToken);

                config.ExpectedFileContent.ShouldContain("bearer_token_env_var");
                config.ExpectedFileContent.ShouldNotContain(PatValue); // env-var indirection keeps the secret out of the file
                logger.Warnings.ShouldBeEmpty();
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        // --- helpers ---

        private static Dictionary<string, JsonNode?> ReadServers(string configPath, string bodyKey)
        {
            var root = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            var body = root[bodyKey]!.AsObject();
            return body.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<(LogLevel Level, string Message)> _entries = new();
            public IEnumerable<string> Warnings => _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _entries.Add((logLevel, formatter(state, exception)));

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
