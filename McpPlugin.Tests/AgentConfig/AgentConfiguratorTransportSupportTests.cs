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
    /// Covers per-agent transport support (Claude Desktop is stdio-only), the post-configure restart
    /// notice, and <see cref="AgentConfiguratorSettings.NormalizeLocalHost"/>.
    /// </summary>
    public class AgentConfiguratorTransportSupportTests
    {
        private static AgentConfiguratorSettings Settings() => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: Path.GetTempPath(),
            executableFullPath: "C:/Tools/srv.exe",
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000");

        private static AgentConfiguratorSettings CloudSettings() => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: Path.GetTempPath(),
            executableFullPath: "C:/Tools/srv.exe",
            port: 50000,
            timeoutMs: 30000,
            host: "https://ai-game.dev/mcp",
            connectionMode: ConnectionMode.Cloud);

        [Fact]
        public void ClaudeDesktop_LocalHttpDescription_IsOnlyTheUnsupportedAlert()
        {
            var desc = new ClaudeDesktopConfigurator().Describe(Settings(), TransportMethod.streamableHttp);

            desc.SetupKind.ShouldBe(AgentSetupKind.Unsupported);
            desc.TransportSupported.ShouldBeFalse();
            desc.Status.ShouldBe(ConfiguratorStatus.NotConfigured);
            desc.IsConfigured.ShouldBeFalse();
            desc.RestartNotice.ShouldBeNull();
            desc.Links.ShouldNotBeEmpty();

            var section = desc.Sections.ShouldHaveSingleItem();
            section.ExpandedFirst.ShouldBeTrue();
            var item = section.Items.ShouldHaveSingleItem();
            item.Kind.ShouldBe(ConfigurationItemKind.Alert);
            item.Text.ShouldContain("stdio");
            item.Text.ShouldContain("Cloud");
        }

        [Fact]
        public void ClaudeDesktop_CloudHttpDescription_IsConnectorSteps_WithThePinnedCloudUrl()
        {
            var settings = CloudSettings().WithProjectKey("agd_pk_secret_value");
            var desc = new ClaudeDesktopConfigurator().Describe(settings, TransportMethod.streamableHttp);

            desc.SetupKind.ShouldBe(AgentSetupKind.Manual);
            desc.TransportSupported.ShouldBeFalse(); // nothing to write: no Configure
            desc.Status.ShouldBe(ConfiguratorStatus.NotConfigured);
            desc.RestartNotice.ShouldBeNull();

            var steps = desc.Sections[0];
            steps.ExpandedFirst.ShouldBeTrue();
            steps.Items.ShouldContain(i => i.Kind == ConfigurationItemKind.Link && i.Url == ClaudeDesktopConfigurator.ConnectorsSettingsUrl);
            var url = steps.Items.Single(i => i.Kind == ConfigurationItemKind.ReadOnlyField).Text;
            url.ShouldBe(settings.PinnedHttpUrl);
            desc.Sections.SelectMany(s => s.Items).ShouldNotContain(i => i.Text.Contains("agd_pk_secret_value"));
            desc.Sections.SelectMany(s => s.Items).ShouldNotContain(i => i.Kind == ConfigurationItemKind.Alert);
        }

        [Fact]
        public void ClaudeDesktop_StdioDescription_IsSupported()
        {
            var desc = new ClaudeDesktopConfigurator().Describe(Settings(), TransportMethod.stdio);

            desc.TransportSupported.ShouldBeTrue();
            desc.Sections.ShouldContain(s => s.Heading == "Configuration");
        }

        [Fact]
        public void EveryOtherAgent_SupportsBothTransports()
        {
            foreach (var c in AiAgentConfiguratorRegistry.All.Where(c => c is not ClaudeDesktopConfigurator))
            {
                c.SupportsTransport(TransportMethod.stdio).ShouldBeTrue(c.AgentName);
                c.SupportsTransport(TransportMethod.streamableHttp).ShouldBeTrue(c.AgentName);
                c.Describe(Settings(), TransportMethod.streamableHttp).TransportSupported.ShouldBeTrue(c.AgentName);
            }
        }

        [Fact]
        public void EveryAgentThatWritesAConfig_HasRestartNotice_ForStdio()
        {
            foreach (var c in AiAgentConfiguratorRegistry.All.Where(c => c is not CustomConfigurator))
            {
                var notice = c.GetRestartNotice(TransportMethod.stdio);
                notice.ShouldNotBeNullOrWhiteSpace(c.AgentName);
                notice!.ShouldContain(c.AgentName);
                c.Describe(Settings(), TransportMethod.stdio).RestartNotice.ShouldBe(notice, c.AgentName);
            }
        }

        [Fact]
        public void HttpTransport_HasNoRestartNotice()
        {
            var c = new ClaudeDesktopConfigurator();
            c.GetRestartNotice(TransportMethod.streamableHttp).ShouldBeNull();
            new ClaudeCodeConfigurator().Describe(Settings(), TransportMethod.streamableHttp).RestartNotice.ShouldBeNull();
        }

        [Fact]
        public void Custom_HasNoRestartNotice()
            => new CustomConfigurator().GetRestartNotice(TransportMethod.stdio).ShouldBeNull();

        /// <summary>A stdio-only agent like Claude Desktop, but with its config file in a temp folder.</summary>
        private sealed class StdioOnlyTempConfigurator : AiAgentConfigurator
        {
            private readonly string _configPath;
            public StdioOnlyTempConfigurator(string configPath) => _configPath = configPath;
            public override string AgentName => "Stdio Only";
            public override string AgentId => "stdio-only-test";
            public override string DownloadUrl => string.Empty;
            public override string? IconName => null;
            public override bool SupportsTransport(TransportMethod transport) => transport == TransportMethod.stdio;
            protected override AiAgentConfig CreateStdioConfig(AgentConfiguratorSettings settings, Microsoft.Extensions.Logging.ILogger? logger)
                => AgentConfigBuilders.JsonStdio(AgentName, _configPath, settings, logger);
            protected override AiAgentConfig CreateHttpConfig(AgentConfiguratorSettings settings, Microsoft.Extensions.Logging.ILogger? logger)
                => AgentConfigBuilders.JsonHttp(AgentName, _configPath, settings, logger);
            protected override System.Collections.Generic.IReadOnlyList<ConfigurationSection> BuildSections(
                AgentConfiguratorSettings settings, TransportMethod transport, Microsoft.Extensions.Logging.ILogger? logger)
                => DefaultConfigurationSections(settings, transport, logger);
        }

        [Fact]
        public void UnsupportedTransport_MatchingEntryOnDisk_IsNotConfigured_ButStillDetected()
        {
            var dir = Path.Combine(Path.GetTempPath(), "mcp-transport-support-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var c = new StdioOnlyTempConfigurator(Path.Combine(dir, "config.json"));
                var settings = Settings();
                var http = c.GetHttpConfig(settings, credentialMode: settings.ResolveHttpCredentialMode());
                http.Configure().ShouldBeTrue(); // e.g. an entry an older plugin wrote
                http.IsConfigured().ShouldBeTrue();

                c.IsConfigured(settings, TransportMethod.streamableHttp).ShouldBeFalse();
                c.GetStatus(settings, TransportMethod.streamableHttp).ShouldBe(ConfiguratorStatus.NotConfigured);
                c.IsDetected(settings).ShouldBeTrue(); // still found, so the engine can offer Remove
                c.GetStatus(settings, TransportMethod.stdio).ShouldBe(ConfiguratorStatus.ReconfigureNeeded);
            }
            finally { Directory.Delete(dir, recursive: true); }
        }

        [Theory]
        [InlineData(null, "http://localhost:5250")]
        [InlineData("", "http://localhost:5250")]
        [InlineData("   ", "http://localhost:5250")]
        [InlineData("http://localhost/mcp", "http://localhost:5250")]
        [InlineData("http://localhost", "http://localhost:5250")]
        [InlineData("http://localhost/", "http://localhost:5250")]
        [InlineData("http://127.0.0.1/mcp", "http://127.0.0.1:5250")]
        [InlineData("https://localhost/mcp", "https://localhost:5250")]
        [InlineData("http://[::1]/mcp", "http://[::1]:5250")]
        [InlineData(" http://localhost/mcp ", "http://localhost:5250")]
        public void NormalizeLocalHost_FillsTheDerivedPort(string? host, string expected)
            => AgentConfiguratorSettings.NormalizeLocalHost(host, 5250).ShouldBe(expected);

        [Theory]
        [InlineData("http://localhost:8080")]
        [InlineData("http://localhost:80/mcp")]   // an explicitly typed port is user intent
        [InlineData("http://localhost:5250/custom")]
        [InlineData("https://ai-game.dev/mcp")]   // not loopback
        [InlineData("http://192.168.1.10/mcp")]   // remote server on the LAN
        [InlineData("localhost")]                  // not an absolute URL — left for the user to fix
        [InlineData("http://localhost@evil.com/mcp")] // user info: not a loopback URL
        public void NormalizeLocalHost_LeavesOtherUrlsUnchanged(string host)
            => AgentConfiguratorSettings.NormalizeLocalHost(host, 5250).ShouldBe(host);
    }
}
