/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.IO;
using System.Linq;
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Consts.MCP.Server.TransportMethod;

    /// <summary>
    /// Covers the redesigned engine-agnostic configurator surface: the registry shape,
    /// the settings-driven config building, and the UI-description DTO (incl. the new
    /// <see cref="ConfigurationItemKind.EditableField"/> for the Custom agent).
    /// </summary>
    public class AiAgentConfiguratorTests
    {
        private static AgentConfiguratorSettings Settings(string root) => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: root,
            executableFullPath: "C:/Tools/ai-game-developer-mcp-server.exe",
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp");

        [Fact]
        public void Registry_Has18Configurators_CustomLast()
        {
            AiAgentConfiguratorRegistry.All.Count.ShouldBe(16); // 15 sorted + Custom
            AiAgentConfiguratorRegistry.All.Last().ShouldBeOfType<CustomConfigurator>();
        }

        [Fact]
        public void Registry_LookupByIdAndName_Work()
        {
            AiAgentConfiguratorRegistry.GetByAgentId("claude-code").ShouldBeOfType<ClaudeCodeConfigurator>();
            AiAgentConfiguratorRegistry.GetByAgentName("Codex").ShouldBeOfType<CodexConfigurator>();
            AiAgentConfiguratorRegistry.GetByAgentId("nope").ShouldBeNull();
            AiAgentConfiguratorRegistry.GetIndexByAgentId("claude-code").ShouldBeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void AgentIdsAndNames_AreUnique()
        {
            AiAgentConfiguratorRegistry.GetAgentIds().Distinct().Count().ShouldBe(AiAgentConfiguratorRegistry.All.Count);
            AiAgentConfiguratorRegistry.GetAgentNames().Distinct().Count().ShouldBe(AiAgentConfiguratorRegistry.All.Count);
        }

        [Fact]
        public void Configurator_BuildsAndConfigures_StdioAndHttp()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Settings(root);

                var stdio = c.GetStdioConfig(settings);
                stdio.Configure().ShouldBeTrue();
                c.IsConfigured(settings, TransportMethod.stdio).ShouldBeTrue();

                var http = c.GetHttpConfig(settings);
                http.Configure().ShouldBeTrue();
                c.IsConfigured(settings, TransportMethod.streamableHttp).ShouldBeTrue();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        // --- mcp-authorize i1 (BUG-A): the status validator must be token-aware. A LOCAL server in
        // the offline `token` auth mode writes an Authorization: Bearer header (HttpCredentialMode
        // .AccessToken); the "expected" config a status check builds must resolve the SAME credential
        // mode from settings, else a correctly-written token config reads back as ReconfigureNeeded
        // forever. These two tests pin the round-trip and the stale-token signal. ---

        private static AgentConfiguratorSettings TokenSettings(string root, string token) => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: root,
            executableFullPath: "C:/Tools/srv.exe",
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp",
            token: token,
            connectionMode: ConnectionMode.Local,
            authOption: Consts.MCP.Server.AuthOption.token);

        [Fact]
        public void TokenMode_HttpConfig_RoundTrips_AsConfigured()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = TokenSettings(root, "local-secret");

                // Local + token mode resolves to the Bearer-header (AccessToken) credential mode.
                settings.ResolveHttpCredentialMode().ShouldBe(HttpCredentialMode.AccessToken);

                // Write exactly what the engine Configure button writes for these settings.
                c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode())
                    .Configure().ShouldBeTrue();

                // Regression: the token-mode config must read back as Configured — no spurious banner.
                c.IsConfigured(settings, TransportMethod.streamableHttp).ShouldBeTrue();
                c.GetStatus(settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.Configured);
                c.Describe(settings, TransportMethod.streamableHttp).Status.ShouldBe(ConfiguratorStatus.Configured);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void TokenMode_StaleToken_ReadsAsReconfigureNeeded()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new ClaudeCodeConfigurator();

                // Configure was run earlier with the OLD local token.
                var oldSettings = TokenSettings(root, "old-secret");
                c.GetHttpConfig(oldSettings, credentialMode: oldSettings.ResolveHttpCredentialMode())
                    .Configure().ShouldBeTrue();

                // The current LocalToken has since rotated — same project, new token value.
                var newSettings = TokenSettings(root, "new-secret");

                // The on-disk Bearer <old> no longer matches the expected Bearer <new>: detected, stale.
                c.IsConfigured(newSettings, TransportMethod.streamableHttp).ShouldBeFalse();
                c.GetStatus(newSettings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.ReconfigureNeeded);
                c.Describe(newSettings, TransportMethod.streamableHttp).Status.ShouldBe(ConfiguratorStatus.ReconfigureNeeded);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Codex_IsTomlBased_AndAdvancedPatInjectsEnvVar_ButDefaultPathDoesNot()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new CodexConfigurator();
                var settings = new AgentConfiguratorSettings(
                    OperatingSystemKind.Windows, root, "C:/Tools/srv.exe", 50000, 30000,
                    "http://localhost:50000/mcp", token: "secret",
                    authOption: Consts.MCP.Server.AuthOption.required);

                // Default (OAuth) path: credential-free — no env-var indirection, no token value.
                var oauth = c.GetHttpConfig(settings);
                oauth.ShouldBeOfType<TomlAiAgentConfig>();
                oauth.ExpectedFileContent.ShouldNotContain("bearer_token_env_var");
                oauth.ExpectedFileContent.ShouldNotContain("secret");

                // Advanced PAT path: env-var indirection is written; the token VALUE stays out of the file.
                var pat = c.GetHttpConfig(settings, credentialMode: HttpCredentialMode.AccessToken);
                pat.ExpectedFileContent.ShouldContain("bearer_token_env_var");
                pat.ExpectedFileContent.ShouldNotContain("secret");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Description_CarriesIdentityIconNameAndSections()
        {
            var c = new ClaudeCodeConfigurator();
            var desc = c.Describe(Settings(Path.GetTempPath()), TransportMethod.stdio);

            desc.AgentName.ShouldBe("Claude Code");
            desc.AgentId.ShouldBe("claude-code");
            desc.IconName.ShouldBe("claude-64.png"); // name, not bytes
            desc.Sections.ShouldNotBeEmpty();
            desc.Sections[0].ExpandedFirst.ShouldBeTrue();
        }

        [Fact]
        public void Custom_Description_ExposesEditableFieldForSkillsPath()
        {
            var c = new CustomConfigurator { EditableSkillsPath = ".my/skills" };
            var desc = c.Describe(Settings(Path.GetTempPath()), TransportMethod.stdio);

            desc.IsConfigured.ShouldBeFalse();
            var editable = desc.Sections
                .SelectMany(s => s.Items)
                .Single(i => i.Kind == ConfigurationItemKind.EditableField);
            editable.Text.ShouldBe(".my/skills");
        }

        [Fact]
        public void CloudMode_ForcesHttpAuthRequired()
        {
            var settings = new AgentConfiguratorSettings(
                OperatingSystemKind.Linux, "/proj", "/bin/srv", 50000, 30000,
                "https://cloud.ai-game.dev/mcp", token: "tok",
                connectionMode: ConnectionMode.Cloud);
            settings.IsHttpAuthRequired.ShouldBeTrue();
        }

        [Fact]
        public void AllConfigurators_DescribeWithoutThrowing_BothTransports()
        {
            var settings = Settings(Path.GetTempPath());
            foreach (var c in AiAgentConfiguratorRegistry.All)
            {
                c.Describe(settings, TransportMethod.stdio).Sections.ShouldNotBeNull();
                c.Describe(settings, TransportMethod.streamableHttp).Sections.ShouldNotBeNull();
            }
        }
    }
}
