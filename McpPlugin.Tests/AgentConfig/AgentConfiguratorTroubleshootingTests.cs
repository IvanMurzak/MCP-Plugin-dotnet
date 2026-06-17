/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak)                    │
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
    /// Covers the per-agent Troubleshooting sections and the Custom agent's Docker setup/stop/remove
    /// commands ported from Unity's configurators (issue #131). The Troubleshooting content is
    /// appended after the configuration sections by <see cref="AiAgentConfigurator.Describe"/>.
    /// </summary>
    public class AgentConfiguratorTroubleshootingTests
    {
        private static AgentConfiguratorSettings Settings(string? root = null, string executable = "C:/Tools/srv.exe") => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: root ?? Path.GetTempPath(),
            executableFullPath: executable,
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp");

        private static ConfigurationSection? Troubleshooting(AiAgentConfigurator c, TransportMethod transport)
            => c.Describe(Settings(), transport).Sections.FirstOrDefault(s => s.Heading == "Troubleshooting");

        [Theory]
        [InlineData(TransportMethod.stdio)]
        [InlineData(TransportMethod.streamableHttp)]
        public void ClaudeCode_HasTroubleshootingSection(TransportMethod transport)
        {
            var section = Troubleshooting(new ClaudeCodeConfigurator(), transport);
            section.ShouldNotBeNull();
            section!.ExpandedFirst.ShouldBeFalse();
            section.Items.ShouldAllBe(i => i.Kind == ConfigurationItemKind.Description);
            section.Items.ShouldContain(i => i.Text.Contains("Check that the configuration file .mcp.json exists"));
            section.Items.ShouldContain(i => i.Text.Contains("Restart Claude Code after configuration changes"));
        }

        [Theory]
        [InlineData(TransportMethod.stdio)]
        [InlineData(TransportMethod.streamableHttp)]
        public void Cursor_HasTroubleshootingSection(TransportMethod transport)
        {
            var section = Troubleshooting(new CursorConfigurator(), transport);
            section.ShouldNotBeNull();
            section!.Items.ShouldContain(i => i.Text.Contains("'.cursor/mcp.json' file must have no json syntax errors."));
        }

        [Theory]
        [InlineData(TransportMethod.stdio)]
        [InlineData(TransportMethod.streamableHttp)]
        public void Codex_HasTroubleshootingSection(TransportMethod transport)
        {
            var section = Troubleshooting(new CodexConfigurator(), transport);
            section.ShouldNotBeNull();
            section!.Items.ShouldContain(i => i.Text.Contains("Ensure Codex CLI is installed and accessible from terminal"));
        }

        [Fact]
        public void Gemini_StdioTroubleshooting_HasDebugHint_HttpDoesNot()
        {
            var c = new GeminiConfigurator();
            var stdio = Troubleshooting(c, TransportMethod.stdio);
            var http = Troubleshooting(c, TransportMethod.streamableHttp);

            stdio.ShouldNotBeNull();
            http.ShouldNotBeNull();
            stdio!.Items.ShouldContain(i => i.Text.Contains("--debug flag"));
            http!.Items.ShouldNotContain(i => i.Text.Contains("--debug flag"));
        }

        [Fact]
        public void Kilo_StdioTroubleshooting_HasExecutablePathLine_HttpDoesNot()
        {
            var c = new KiloCodeConfigurator();
            var stdio = Troubleshooting(c, TransportMethod.stdio);
            var http = Troubleshooting(c, TransportMethod.streamableHttp);

            stdio.ShouldNotBeNull();
            http.ShouldNotBeNull();
            stdio!.Items.ShouldContain(i => i.Text.Contains("Check that the executable path is correct."));
            http!.Items.ShouldNotContain(i => i.Text.Contains("Check that the executable path is correct."));
        }

        [Fact]
        public void ClaudeDesktop_HasTroubleshooting_ForStdioOnly()
        {
            var c = new ClaudeDesktopConfigurator();
            Troubleshooting(c, TransportMethod.stdio).ShouldNotBeNull();
            Troubleshooting(c, TransportMethod.streamableHttp).ShouldBeNull();
        }

        [Fact]
        public void Rider_HasTroubleshooting_ForStdioOnly()
        {
            var c = new RiderConfigurator();
            var stdio = Troubleshooting(c, TransportMethod.stdio);
            stdio.ShouldNotBeNull();
            stdio!.Items.ShouldContain(i => i.Text.Contains("Restart Rider after configuration changes"));
            Troubleshooting(c, TransportMethod.streamableHttp).ShouldBeNull();
        }

        [Fact]
        public void AllNonCustomAgents_EmitTroubleshooting_OnAtLeastOneTransport()
        {
            foreach (var c in AiAgentConfiguratorRegistry.All)
            {
                if (c is CustomConfigurator)
                    continue; // Custom has Docker commands instead of a Troubleshooting foldout (matches Unity).

                var hasAny = Troubleshooting(c, TransportMethod.stdio) != null
                    || Troubleshooting(c, TransportMethod.streamableHttp) != null;
                hasAny.ShouldBeTrue($"{c.AgentName} should emit a Troubleshooting section on at least one transport.");
            }
        }

        [Fact]
        public void Troubleshooting_IsAppendedAfterConfigurationSections()
        {
            var desc = new ClaudeCodeConfigurator().Describe(Settings(), TransportMethod.stdio);
            var headings = desc.Sections.Select(s => s.Heading).ToList();

            headings.ShouldContain("Troubleshooting");
            // Troubleshooting comes after the configuration ("Start" / "Manual Configuration Steps") sections.
            headings.Last().ShouldBe("Troubleshooting");
        }

        // ---- Custom agent Docker commands ----

        [Fact]
        public void Custom_HttpConfiguration_EmitsDockerSetupStartStopRemoveCommands()
        {
            var settings = Settings();
            var desc = new CustomConfigurator().Describe(settings, TransportMethod.streamableHttp);
            var values = desc.Sections
                .SelectMany(s => s.Items)
                .Where(i => i.Kind == ConfigurationItemKind.ReadOnlyField)
                .Select(i => i.Text)
                .ToList();

            var container = $"gamedev-mcp-server-{settings.Port}";
            values.ShouldContain(v => v.StartsWith("docker run -d") && v.Contains(container) && v.Contains("aigamedeveloper/mcp-server:8.0.0"));
            values.ShouldContain($"docker start {container}");
            values.ShouldContain($"docker stop {container}");
            values.ShouldContain($"docker rm {container}");
        }

        [Fact]
        public void Custom_DockerSetupRun_IncludesPortMappingAndEnvVars()
        {
            var settings = Settings();
            var cmd = DockerCommands.SetupRun(settings);

            cmd.ShouldContain($"-p {settings.Port}:{settings.Port}");
            cmd.ShouldContain($"-e {Consts.MCP.Server.Env.Port}={settings.Port}");
            cmd.ShouldContain($"-e {Consts.MCP.Server.Env.PluginTimeout}={settings.TimeoutMs}");
            cmd.ShouldContain($"-e {Consts.MCP.Server.Env.ClientTransportMethod}={Consts.MCP.Server.TransportMethod.streamableHttp}");
        }

        [Fact]
        public void Custom_DockerSetupRun_IncludesTokenEnvVar_OnlyWhenAuthRequiredWithToken()
        {
            var with = new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: Path.GetTempPath(),
                executableFullPath: "C:/Tools/srv.exe",
                port: 50000,
                timeoutMs: 30000,
                host: "http://localhost:50000/mcp",
                token: "secret-token",
                authOption: Consts.MCP.Server.AuthOption.required);
            DockerCommands.SetupRun(with).ShouldContain($"-e {Consts.MCP.Server.Env.Token}=secret-token");

            // No token => no token env var, even when auth is required.
            var without = new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: Path.GetTempPath(),
                executableFullPath: "C:/Tools/srv.exe",
                port: 50000,
                timeoutMs: 30000,
                host: "http://localhost:50000/mcp",
                authOption: Consts.MCP.Server.AuthOption.required);
            DockerCommands.SetupRun(without).ShouldNotContain($"-e {Consts.MCP.Server.Env.Token}=");
        }

        [Fact]
        public void Custom_DockerImage_RespectsEngineSuppliedVersionAndImage()
        {
            var settings = new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: Path.GetTempPath(),
                executableFullPath: "C:/Tools/srv.exe",
                port: 12345,
                timeoutMs: 30000,
                host: "http://localhost:12345/mcp",
                serverExecutableName: "custom-server",
                serverVersion: "9.1.2",
                dockerImage: "myrepo/mcp");

            DockerCommands.SetupRun(settings).ShouldContain("myrepo/mcp:9.1.2");
            DockerCommands.Run(settings).ShouldBe("docker start custom-server-12345");
        }
    }
}
