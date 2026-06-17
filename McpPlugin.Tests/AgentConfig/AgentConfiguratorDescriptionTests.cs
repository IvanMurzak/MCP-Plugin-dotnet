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
    /// Covers the GAP-3 DTO extensions: open-URL links (download + tutorial) and the three-state
    /// configuration status (absent / configured-current / configured-but-stale), ported from
    /// Unity's <c>AiAgentConfigurator</c> link emission and <c>IsReconfigureNeeded</c> logic.
    /// </summary>
    public class AgentConfiguratorDescriptionTests
    {
        private static AgentConfiguratorSettings Settings(string root, string executable = "C:/Tools/srv.exe") => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: root,
            executableFullPath: executable,
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp");

        [Fact]
        public void Description_WithTutorial_CarriesDownloadAndTutorialLinks()
        {
            var c = new ClaudeCodeConfigurator();
            var desc = c.Describe(Settings(Path.GetTempPath()), TransportMethod.stdio);

            desc.Links.Count.ShouldBe(2);
            desc.Links.ShouldAllBe(l => l.Kind == ConfigurationItemKind.Link);
            desc.Links.ShouldContain(l => l.Url == c.DownloadUrl);
            desc.Links.ShouldContain(l => l.Url == c.TutorialUrl);
        }

        [Fact]
        public void Description_WithoutTutorial_CarriesOnlyDownloadLink()
        {
            var c = new KiloCodeConfigurator();
            c.TutorialUrl.ShouldBe(string.Empty);
            var desc = c.Describe(Settings(Path.GetTempPath()), TransportMethod.stdio);

            var link = desc.Links.ShouldHaveSingleItem();
            link.Kind.ShouldBe(ConfigurationItemKind.Link);
            link.Url.ShouldBe(c.DownloadUrl);
        }

        [Fact]
        public void Custom_Description_HasNoLinks()
        {
            var c = new CustomConfigurator();
            c.Describe(Settings(Path.GetTempPath()), TransportMethod.stdio).Links.ShouldBeEmpty();
        }

        [Fact]
        public void Status_NoConfigOnDisk_IsNotConfigured()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new KiloCodeConfigurator();
                var desc = c.Describe(Settings(root), TransportMethod.stdio);

                desc.Status.ShouldBe(ConfiguratorStatus.NotConfigured);
                desc.IsConfigured.ShouldBeFalse();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Status_MatchingConfigOnDisk_IsConfigured()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new KiloCodeConfigurator();
                var settings = Settings(root);
                c.GetStdioConfig(settings).Configure().ShouldBeTrue();

                var desc = c.Describe(settings, TransportMethod.stdio);
                desc.Status.ShouldBe(ConfiguratorStatus.Configured);
                desc.IsConfigured.ShouldBeTrue();
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Status_StaleConfigOnDisk_IsReconfigureNeeded_AndPrependsAlertSection()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var c = new KiloCodeConfigurator();

                // Write a config with one executable, then describe with a DIFFERENT executable:
                // the entry is detected on disk but no longer matches the current settings.
                c.GetStdioConfig(Settings(root, executable: "C:/Old/old-server.exe")).Configure().ShouldBeTrue();
                var staleSettings = Settings(root, executable: "C:/New/new-server.exe");

                var desc = c.Describe(staleSettings, TransportMethod.stdio);

                desc.Status.ShouldBe(ConfiguratorStatus.ReconfigureNeeded);
                desc.IsConfigured.ShouldBeFalse();

                var alert = desc.Sections
                    .SelectMany(s => s.Items)
                    .Single(i => i.Kind == ConfigurationItemKind.Alert);
                alert.Text.ShouldContain("outdated");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
