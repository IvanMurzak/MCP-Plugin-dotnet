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
                var settings = Settings(root);
                var pin = settings.ProjectPin;
                var port = settings.ResolvedPort;

                var stdio = c.GetStdioConfig(settings).ExpectedFileContent;
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Port}={port}\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.PluginTimeout}=30000\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio\"");
                stdio.ShouldContain($"\"{Consts.MCP.Server.Args.Project}={pin}\"");
                stdio.ShouldNotContain("\"type\"");
                stdio.ShouldNotContain("\"url\"");

                var http = c.GetHttpConfig(settings).ExpectedFileContent;
                http.ShouldContain($"\"url\": \"http://localhost:{port}/mcp/p/{pin}\"");
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

        [Fact]
        public void MarkerPortOverride_PropagatesToWrittenPort_StdioAndHttp()
        {
            var root = NewTempDir();
            try
            {
                const int overridePort = 27777;
                new ProjectMarker { PortOverride = overridePort }.Write(root);

                var settings = Settings(root);
                settings.ResolvedPort.ShouldBe(overridePort);
                settings.Identity.PortIsOverridden.ShouldBeTrue();

                var c = new ClaudeCodeConfigurator();
                c.GetStdioConfig(settings).ExpectedFileContent.ShouldContain($"{Consts.MCP.Server.Args.Port}={overridePort}");
                c.GetHttpConfig(settings).ExpectedFileContent.ShouldContain($"localhost:{overridePort}/");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        [Fact]
        public void NoMarker_UsesDeterministicDerivedPort()
        {
            var root = NewTempDir();
            try
            {
                var settings = Settings(root);
                settings.Identity.PortIsOverridden.ShouldBeFalse();
                settings.ResolvedPort.ShouldBe(ProjectIdentity.DerivePort(root));
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
