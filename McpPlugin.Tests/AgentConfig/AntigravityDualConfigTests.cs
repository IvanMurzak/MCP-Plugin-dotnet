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
    /// Antigravity reads its global MCP config from ONE of two unpredictable locations
    /// (<c>~/.gemini/config/mcp_config.json</c> or <c>~/.gemini/antigravity/mcp_config.json</c>), so the
    /// configurator writes both. Status: configured ⇔ at least one file exists AND every existing file is
    /// correctly configured. Remove: clean every existing file, never create one.
    /// </summary>
    public sealed class AntigravityDualConfigTests : IDisposable
    {
        private const string Key = "agd_pk_AntigravityDualProbeKey_0123456789";
        private const string CloudHost = "https://ai-game.dev/mcp";
        private const string ServerName = AiAgentConfig.DefaultMcpServerName;

        private readonly string _home;
        private readonly string _pathA;
        private readonly string _pathB;
        private readonly AntigravityConfigurator _c;
        private readonly AgentConfiguratorSettings _settings;

        public AntigravityDualConfigTests()
        {
            _home = Path.Combine(Path.GetTempPath(), "agd-antigravity-" + Guid.NewGuid().ToString("N"));
            _pathA = Path.Combine(_home, ".gemini", "config", "mcp_config.json");
            _pathB = Path.Combine(_home, ".gemini", "antigravity", "mcp_config.json");
            _c = new AntigravityConfigurator(_home);
            _settings = new AgentConfiguratorSettings(
                OperatingSystemKind.Linux, Path.Combine(_home, "proj"), "/opt/srv/gamedev-mcp-server", 50000, 30000, CloudHost,
                connectionMode: ConnectionMode.Cloud).WithProjectKey(Key);
        }

        public void Dispose()
        {
            if (Directory.Exists(_home))
                Directory.Delete(_home, recursive: true);
        }

        private AiAgentConfig Http(AgentConfiguratorSettings? s = null)
        {
            s ??= _settings;
            return _c.GetHttpConfig(s, credentialMode: s.ResolveHttpCredentialMode());
        }

        private static JsonObject? Entry(string path) =>
            JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]?[ServerName]?.AsObject();

        private static void Write(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        /// <summary>Writes a correctly-configured file at <paramref name="path"/> only.</summary>
        private void ConfigureOnly(string path)
        {
            var single = ((CompositeAiAgentConfig)Http()).Configs.Single(c => c.ConfigPath == path);
            single.Configure().ShouldBeTrue();
        }

        private const string StaleJson = "{\"mcpServers\":{\"" + ServerName + "\":{\"serverUrl\":\"https://ai-game.dev/mcp/p/stale\",\"disabled\":false}}}";

        // ── Paths ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConfigPaths_AreBothCandidates_UnderTheUserProfile()
        {
            var config = Http();
            config.ConfigPaths.ShouldBe(new[] { _pathA, _pathB });
            config.ConfigPath.ShouldContain(_pathA);
            config.ConfigPath.ShouldContain(_pathB);
        }

        [Fact]
        public void RegistryInstance_ResolvesBothPaths_FromTheRealUserProfile()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var paths = AiAgentConfiguratorRegistry.GetByAgentId("antigravity")!.GetHttpConfig(_settings).ConfigPaths;
            paths.ShouldBe(new[]
            {
                Path.Combine(home, ".gemini", "config", "mcp_config.json"),
                Path.Combine(home, ".gemini", "antigravity", "mcp_config.json")
            });
        }

        [Fact]
        public void ManualSetupText_NamesBothPaths()
        {
            var texts = _c.Describe(_settings, TransportMethod.streamableHttp).Sections
                .SelectMany(s => s.Items).Select(i => i.Text ?? string.Empty).ToList();
            texts.ShouldContain(t => t.Contains(_pathA));
            texts.ShouldContain(t => t.Contains(_pathB));
        }

        // ── Configure ──────────────────────────────────────────────────────────

        [Fact]
        public void Configure_WritesBothFiles_WithTheProjectKeyHeader()
        {
            Http().Configure().ShouldBeTrue();

            foreach (var path in new[] { _pathA, _pathB })
            {
                File.Exists(path).ShouldBeTrue(path);
                var entry = Entry(path).ShouldNotBeNull(path);
                entry["serverUrl"]!.GetValue<string>().ShouldContain("/mcp/p/" + _settings.ProjectPin);
                entry["disabled"]!.GetValue<bool>().ShouldBeFalse();
                entry["headers"]!["Authorization"]!.GetValue<string>().ShouldBe("Bearer " + Key);
            }
        }

        [Fact]
        public void Configure_Stdio_WritesBothFiles()
        {
            _c.GetStdioConfig(_settings).Configure().ShouldBeTrue();
            foreach (var path in new[] { _pathA, _pathB })
            {
                var entry = Entry(path).ShouldNotBeNull(path);
                entry["command"]!.GetValue<string>().ShouldBe("/opt/srv/gamedev-mcp-server");
                entry.ContainsKey("serverUrl").ShouldBeFalse();
                entry.ContainsKey("headers").ShouldBeFalse();
            }
            _c.IsConfigured(_settings, TransportMethod.stdio).ShouldBeTrue();
        }

        [Fact]
        public void Configure_PreservesOtherEntriesAndFields_InEachFile()
        {
            Write(_pathA, "{\"theme\":\"dark\",\"mcpServers\":{\"other-a\":{\"command\":\"a\"}}}");
            Write(_pathB, "{\"mcpServers\":{\"other-b\":{\"serverUrl\":\"https://b.example/mcp\"}},\"x\":1}");

            Http().Configure().ShouldBeTrue();

            var a = JsonNode.Parse(File.ReadAllText(_pathA))!;
            a["theme"]!.GetValue<string>().ShouldBe("dark");
            a["mcpServers"]!["other-a"]!["command"]!.GetValue<string>().ShouldBe("a");
            a["mcpServers"]![ServerName].ShouldNotBeNull();

            var b = JsonNode.Parse(File.ReadAllText(_pathB))!;
            b["x"]!.GetValue<int>().ShouldBe(1);
            b["mcpServers"]!["other-b"]!["serverUrl"]!.GetValue<string>().ShouldBe("https://b.example/mcp");
            b["mcpServers"]![ServerName].ShouldNotBeNull();
        }

        [Fact]
        public void Configure_ReportsTheFailingPath_AndStillWritesTheOther()
        {
            // Make B unwritable: a FILE where B's parent directory must be.
            Directory.CreateDirectory(Path.Combine(_home, ".gemini"));
            File.WriteAllText(Path.Combine(_home, ".gemini", "antigravity"), "not a directory");

            var config = (CompositeAiAgentConfig)Http();
            config.Configure().ShouldBeFalse();
            config.FailedConfigPaths.ShouldBe(new[] { _pathB });
            Entry(_pathA).ShouldNotBeNull(); // the other file was still written
        }

        // ── Status ─────────────────────────────────────────────────────────────

        [Fact]
        public void Status_NoFile_IsNotConfigured()
        {
            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeFalse();
            _c.GetStatus(_settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.NotConfigured);
        }

        [Fact]
        public void Status_OnlyA_Configured_IsConfigured()
        {
            ConfigureOnly(_pathA);
            File.Exists(_pathB).ShouldBeFalse();
            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeTrue();
        }

        [Fact]
        public void Status_OnlyB_Configured_IsConfigured()
        {
            ConfigureOnly(_pathB);
            File.Exists(_pathA).ShouldBeFalse();
            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeTrue();
        }

        [Fact]
        public void Status_Both_Configured_IsConfigured()
        {
            Http().Configure().ShouldBeTrue();
            _c.GetStatus(_settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.Configured);
        }

        [Fact]
        public void Status_AConfigured_BStale_IsReconfigureNeeded_AndConfigureRepairsBoth()
        {
            ConfigureOnly(_pathA);
            Write(_pathB, StaleJson);

            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeFalse();
            _c.GetStatus(_settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.ReconfigureNeeded);

            Http().Configure().ShouldBeTrue();
            _c.GetStatus(_settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.Configured);
        }

        [Fact]
        public void Status_BConfigured_AStale_IsNotConfigured()
        {
            Write(_pathA, StaleJson);
            ConfigureOnly(_pathB);
            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeFalse();
        }

        [Fact]
        public void Status_AConfigured_BExistsWithoutEntry_IsNotConfigured()
        {
            ConfigureOnly(_pathA);
            Write(_pathB, "{\"mcpServers\":{}}");
            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeFalse();
        }

        [Fact]
        public void Status_MissingProjectKeyHeader_InOneFile_IsNotConfigured()
        {
            Http().Configure().ShouldBeTrue();
            // Re-write B without the header (the credential-free shape) — B is now stale for a project-key config.
            ((CompositeAiAgentConfig)_c.GetHttpConfig(_settings, credentialMode: HttpCredentialMode.Oauth))
                .Configs.Single(c => c.ConfigPath == _pathB).Configure().ShouldBeTrue();
            Entry(_pathB)!.ContainsKey("headers").ShouldBeFalse();

            _c.IsConfigured(_settings, TransportMethod.streamableHttp).ShouldBeFalse();
        }

        // ── Remove ─────────────────────────────────────────────────────────────

        [Fact]
        public void Unconfigure_RemovesFromBoth_KeepsFilesAndOtherEntries()
        {
            Write(_pathA, "{\"mcpServers\":{\"other-a\":{\"command\":\"a\"}}}");
            Write(_pathB, "{\"mcpServers\":{\"other-b\":{\"command\":\"b\"}}}");
            Http().Configure().ShouldBeTrue();

            Http().Unconfigure().ShouldBeTrue();

            foreach (var (path, other) in new[] { (_pathA, "other-a"), (_pathB, "other-b") })
            {
                File.Exists(path).ShouldBeTrue(path);
                var servers = JsonNode.Parse(File.ReadAllText(path))!["mcpServers"]!.AsObject();
                servers.ContainsKey(ServerName).ShouldBeFalse(path);
                servers.ContainsKey(other).ShouldBeTrue(path);
            }
            _c.GetStatus(_settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.NotConfigured);
        }

        [Fact]
        public void Unconfigure_OnlyBPresent_RemovesFromB_NeverCreatesA()
        {
            ConfigureOnly(_pathB);

            Http().Unconfigure().ShouldBeTrue();

            File.Exists(_pathA).ShouldBeFalse();
            Directory.Exists(Path.GetDirectoryName(_pathA)!).ShouldBeFalse();
            Entry(_pathB).ShouldBeNull();
        }
    }
}
