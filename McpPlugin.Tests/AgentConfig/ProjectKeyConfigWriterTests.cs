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
using Shouldly;
using Xunit;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Owner ruling 2026-09-23 / project-keys contract §7: in Cloud mode with a project key EVERY agent's HTTP
    /// config carries <c>Authorization: Bearer agd_pk_…</c> next to the pinned URL, via the resolved default
    /// credential mode — no per-agent opt-in. One parameterized test over the whole registry, plus the
    /// both-halves control (no key ⇒ URL-only) and round-trips for the project-local writers.
    /// </summary>
    public sealed class ProjectKeyConfigWriterTests
    {
        private const string Key = "agd_pk_ConfigWriterProbeKey_0123456789";
        private const string CloudHost = "https://ai-game.dev/mcp";

        private static AgentConfiguratorSettings Cloud(string root, string? key = Key) => new AgentConfiguratorSettings(
            OperatingSystemKind.Linux, root, "/opt/srv/gamedev-mcp-server", 50000, 30000, CloudHost,
            token: "unrelated-legacy-token", connectionMode: ConnectionMode.Cloud).WithProjectKey(key);

        public static IEnumerable<object[]> HttpConfigurators() =>
            AiAgentConfiguratorRegistry.All
                .Where(c => c is not CustomConfigurator) // no auto-detectable config file at all
                .Select(c => new object[] { c.AgentId });

        [Theory]
        [MemberData(nameof(HttpConfigurators))]
        public void Cloud_WithProjectKey_EveryAgentWritesTheBearerHeader(string agentId)
        {
            var c = AiAgentConfiguratorRegistry.GetByAgentId(agentId)!;
            var settings = Cloud(Path.Combine(Path.GetTempPath(), "agd-pk-writer-" + agentId));

            settings.ResolveHttpCredentialMode().ShouldBe(HttpCredentialMode.AccessToken);
            var content = c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode()).ExpectedFileContent;

            content.ShouldContain("Bearer " + Key, customMessage: agentId);
            content.ShouldContain("/mcp/p/" + settings.ProjectPin, customMessage: agentId);
            content.ShouldNotContain("unrelated-legacy-token", customMessage: agentId); // the project key wins in Cloud
            content.ShouldNotContain("bearer_token_env_var", customMessage: agentId);   // static header, no env setup
        }

        [Theory]
        [MemberData(nameof(HttpConfigurators))]
        public void Cloud_WithoutProjectKey_StaysUrlOnlyOAuth_Control(string agentId)
        {
            var c = AiAgentConfiguratorRegistry.GetByAgentId(agentId)!;
            var settings = Cloud(Path.Combine(Path.GetTempPath(), "agd-pk-writer-" + agentId), key: null);

            settings.ResolveHttpCredentialMode().ShouldBe(HttpCredentialMode.Oauth);
            var content = c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode()).ExpectedFileContent;

            content.ShouldNotContain("Authorization", customMessage: agentId);
            content.ShouldContain("/mcp/p/" + settings.ProjectPin, customMessage: agentId);
        }

        [Fact]
        public void LocalMode_IgnoresAProjectKey()
        {
            var settings = new AgentConfiguratorSettings(OperatingSystemKind.Linux, "/proj", "/srv", 50000, 30000,
                "http://localhost:50000/mcp").WithProjectKey(Key);

            settings.HasProjectKey.ShouldBeFalse();
            settings.ResolveHttpCredentialMode().ShouldBe(HttpCredentialMode.Oauth);
        }

        [Fact]
        public void WithProjectKey_CopiesEverySetting()
        {
            var original = new AgentConfiguratorSettings(OperatingSystemKind.MacOS, "/proj", "/srv", 51000, 12345, CloudHost,
                token: "t", connectionMode: ConnectionMode.Cloud, authOption: AuthOption.oauth, serverExecutableName: "x",
                serverVersion: "9.9.9", dockerImage: "img", localPortDerivation: LocalPortDerivation.V1);
            var copy = original.WithProjectKey(Key);

            copy.ShouldNotBeSameAs(original);
            original.ProjectKey.ShouldBeNull();
            copy.ProjectKey.ShouldBe(Key);
            (copy.OperatingSystem, copy.ProjectRootPath, copy.ExecutableFullPath, copy.Port, copy.TimeoutMs, copy.Host, copy.Token,
             copy.ConnectionMode, copy.AuthOption, copy.ServerExecutableName, copy.ServerVersion, copy.DockerImage, copy.LocalPortDerivation)
                .ShouldBe((original.OperatingSystem, original.ProjectRootPath, original.ExecutableFullPath, original.Port, original.TimeoutMs,
                    original.Host, original.Token, original.ConnectionMode, original.AuthOption, original.ServerExecutableName,
                    original.ServerVersion, original.DockerImage, original.LocalPortDerivation));
            copy.ProjectPin.ShouldBe(original.ProjectPin);
        }

        [Fact]
        public void ClaudeCode_RoundTrip_Configured_ThenANewKeyReadsAsReconfigureNeeded()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new ClaudeCodeConfigurator();
                var settings = Cloud(root);
                c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode()).Configure().ShouldBeTrue();

                var written = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".mcp.json")))!;
                var entry = written["mcpServers"]![AiAgentConfig.DefaultMcpServerName]!;
                ((string)entry["headers"]!["Authorization"]!).ShouldBe("Bearer " + Key);
                ((string)entry["url"]!).ShouldBe("https://ai-game.dev/mcp/p/" + settings.ProjectPin);

                c.GetStatus(settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.Configured);
                c.GetStatus(Cloud(root, "agd_pk_regenerated"), TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.ReconfigureNeeded);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Codex_RoundTrip_WritesStaticHttpHeaders_AndReadsBackConfigured()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new CodexConfigurator();
                var settings = Cloud(root);
                c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode()).Configure().ShouldBeTrue();

                var toml = File.ReadAllText(Path.Combine(root, ".codex", "config.toml"));
                toml.ShouldContain("http_headers = { \"Authorization\" = \"Bearer " + Key + "\" }");
                toml.ShouldNotContain("bearer_token_env_var");

                c.GetStatus(settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.Configured);
                c.GetStatus(Cloud(root, "agd_pk_regenerated"), TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.ReconfigureNeeded);

                // Switching back to a key-less Cloud config removes the header rather than leaving a stale one.
                var oauth = Cloud(root, key: null);
                c.GetHttpConfig(oauth, credentialMode: oauth.ResolveHttpCredentialMode()).Configure().ShouldBeTrue();
                File.ReadAllText(Path.Combine(root, ".codex", "config.toml")).ShouldNotContain("http_headers");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Codex_LocalTokenMode_KeepsTheEnvVarMechanism_Unchanged()
        {
            var settings = new AgentConfiguratorSettings(OperatingSystemKind.Linux, "/proj", "/srv", 50000, 30000,
                "http://localhost:50000/mcp", token: "local-secret", authOption: AuthOption.token);
            var content = new CodexConfigurator().GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode()).ExpectedFileContent;

            content.ShouldContain("bearer_token_env_var");
            content.ShouldNotContain("http_headers");
            content.ShouldNotContain("local-secret");
        }

        [Fact]
        public void DisplayedPreview_MatchesTheWrittenShape_ButRedactsTheKey()
        {
            var settings = Cloud("/proj");
            foreach (var agent in new AiAgentConfigurator[] { new AntigravityConfigurator(), new CodexConfigurator(), new CursorConfigurator() })
            {
                var text = string.Join("\n", agent.Describe(settings, TransportMethod.streamableHttp).Sections
                    .SelectMany(s => s.Items).Select(i => i.Text));
                text.ShouldContain("Bearer " + AgentConfiguratorSettings.ProjectKeyDisplayPlaceholder, customMessage: agent.AgentId);
                text.ShouldNotContain(Key, customMessage: agent.AgentId);
                text.ShouldNotContain("GAME_DEV_AUTH_TOKEN", customMessage: agent.AgentId); // no contradicting env-var steps
            }
        }

        [Fact]
        public void ClaudeCode_ManualCommand_MatchesTheWrittenConfig()
        {
            string Manual(AgentConfiguratorSettings s) => string.Join("\n", new ClaudeCodeConfigurator()
                .Describe(s, TransportMethod.streamableHttp).Sections.SelectMany(x => x.Items).Select(i => i.Text));

            Manual(Cloud("/proj")).ShouldContain("--header \"Authorization: Bearer " + Key + "\"");
            // Cloud WITHOUT a key writes URL-only ⇒ the manual command must not invent a header either.
            Manual(Cloud("/proj", key: null)).ShouldNotContain("Authorization");
        }
    }
}
