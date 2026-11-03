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

        [Fact]
        public void ClaudeDesktop_SupportsStdioOnly()
        {
            var c = new ClaudeDesktopConfigurator();
            c.SupportsTransport(TransportMethod.stdio).ShouldBeTrue();
            c.SupportsTransport(TransportMethod.streamableHttp).ShouldBeFalse();
        }

        [Fact]
        public void ClaudeDesktop_HttpDescription_IsOnlyTheUnsupportedAlert()
        {
            var desc = new ClaudeDesktopConfigurator().Describe(Settings(), TransportMethod.streamableHttp);

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
            item.Text.ShouldContain("Custom");
            // No config preview: nothing an HTTP entry could be copied from.
            desc.Sections.SelectMany(s => s.Items).ShouldNotContain(i => i.Kind == ConfigurationItemKind.ReadOnlyField);
        }

        [Fact]
        public void ClaudeDesktop_StdioDescription_IsSupported_WithRestartNotice()
        {
            var desc = new ClaudeDesktopConfigurator().Describe(Settings(), TransportMethod.stdio);

            desc.TransportSupported.ShouldBeTrue();
            desc.RestartNotice.ShouldNotBeNull();
            desc.RestartNotice!.ShouldContain("Claude Desktop");
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
        public void EveryAgent_HasRestartNotice_ForStdio()
        {
            foreach (var c in AiAgentConfiguratorRegistry.All)
            {
                var notice = c.GetRestartNotice(TransportMethod.stdio);
                notice.ShouldNotBeNullOrWhiteSpace(c.AgentName);
                notice!.ShouldContain(c.AgentName);
                c.Describe(Settings(), TransportMethod.stdio).RestartNotice.ShouldBe(notice, c.AgentName);
            }
        }

        [Fact]
        public void HttpAgents_HaveNoRestartNotice_ByDefault()
        {
            var c = new ClaudeCodeConfigurator();
            c.GetRestartNotice(TransportMethod.streamableHttp).ShouldBeNull();
            c.Describe(Settings(), TransportMethod.streamableHttp).RestartNotice.ShouldBeNull();
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
        public void NormalizeLocalHost_LeavesOtherUrlsUnchanged(string host)
            => AgentConfiguratorSettings.NormalizeLocalHost(host, 5250).ShouldBe(host);
    }
}
