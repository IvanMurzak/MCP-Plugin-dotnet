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
using System.Runtime.InteropServices;
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Common.Consts.MCP.Server.TransportMethod;

    /// <summary>
    /// Per-OS (Windows / macOS / Linux) parity coverage for the engine-agnostic configurators:
    /// asserts the 3-way config-path divergence for Claude Desktop + Cline, that runtime
    /// auto-detection (<see cref="HostOperatingSystem.Detect"/> via <see cref="AgentConfiguratorSettings.CreateForHost"/>)
    /// picks the correct host OS, and that Rider's restored per-OS manual-setup command differs
    /// between Windows and macOS/Linux.
    /// </summary>
    public class AgentConfiguratorOsParityTests
    {
        private static AgentConfiguratorSettings SettingsForOs(OperatingSystemKind os) => new(
            operatingSystem: os,
            projectRootPath: os == OperatingSystemKind.Windows ? "C:/proj" : "/proj",
            executableFullPath: os == OperatingSystemKind.Windows ? "C:/Tools/srv.exe" : "/usr/local/bin/srv",
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp");

        // ── Claude Desktop: Windows %APPDATA%\Claude vs Mac/Linux ~/Library/Application Support/Claude ──

        [Fact]
        public void ClaudeDesktop_StdioConfigPath_DivergesPerOs()
        {
            var c = new ClaudeDesktopConfigurator();

            var winPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.Windows)).ConfigPath;
            var macPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.MacOS)).ConfigPath;
            var linuxPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.Linux)).ConfigPath;

            // Windows uses %APPDATA%\Claude; Mac/Linux both use ~/Library/Application Support/Claude.
            winPath.Replace('\\', '/').ShouldContain("/Claude/claude_desktop_config.json");
            winPath.Replace('\\', '/').ShouldNotContain("Application Support");

            foreach (var p in new[] { macPath, linuxPath })
            {
                p.Replace('\\', '/').ShouldContain("Library/Application Support/Claude/claude_desktop_config.json");
            }
        }

        // ── Cline: 3-way divergence (Win %APPDATA%\Code, Mac ~/Library/Application Support/Code, Linux ~/.config/Code) ──

        [Fact]
        public void Cline_StdioConfigPath_DivergesAcrossAllThreeOs()
        {
            var c = new ClineConfigurator();

            var winPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.Windows)).ConfigPath.Replace('\\', '/');
            var macPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.MacOS)).ConfigPath.Replace('\\', '/');
            var linuxPath = c.GetStdioConfig(SettingsForOs(OperatingSystemKind.Linux)).ConfigPath.Replace('\\', '/');

            // All three must be distinct.
            new[] { winPath, macPath, linuxPath }.Distinct().Count().ShouldBe(3);

            // Windows: %APPDATA%\Code\User\... (no "Application Support", no ".config").
            winPath.ShouldContain("/Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json");
            winPath.ShouldNotContain("Application Support");
            winPath.ShouldNotContain(".config");

            // macOS: ~/Library/Application Support/Code/User/...
            macPath.ShouldContain("Library/Application Support/Code/User/globalStorage");

            // Linux: ~/.config/Code/User/...
            linuxPath.ShouldContain(".config/Code/User/globalStorage");
        }

        // ── Auto-detection: CreateForHost / HostOperatingSystem.Detect picks the real host OS ──

        [Fact]
        public void HostOperatingSystem_Detect_MatchesRuntimeInformation()
        {
            var expected =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OperatingSystemKind.Windows :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OperatingSystemKind.MacOS :
                OperatingSystemKind.Linux;

            HostOperatingSystem.Detect().ShouldBe(expected);
        }

        [Fact]
        public void CreateForHost_AutoDetects_NotHardcodedWindows()
        {
            var settings = AgentConfiguratorSettings.CreateForHost(
                projectRootPath: Path.GetTempPath(),
                executableFullPath: "srv",
                port: 50000,
                timeoutMs: 30000,
                host: "http://localhost:50000/mcp");

            settings.OperatingSystem.ShouldBe(HostOperatingSystem.Detect());

            // On a non-Windows host the auto-detected OS must NOT be the enum's zero-value (Windows).
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                settings.OperatingSystem.ShouldNotBe(OperatingSystemKind.Windows);
        }

        // ── Rider: restored per-OS manual-setup terminal command (Win PowerShell vs Mac/Linux shell) ──

        [Fact]
        public void Rider_StdioManualCommand_IsWindowsPowerShell()
        {
            var c = new RiderConfigurator();
            var fields = ReadOnlyFieldTexts(c, SettingsForOs(OperatingSystemKind.Windows));

            fields.ShouldContain(t => t.Contains("New-Item -ItemType Directory") && t.Contains("Set-Content"));
            fields.ShouldNotContain(t => t.Contains("mkdir -p"));
        }

        [Theory]
        [InlineData(OperatingSystemKind.MacOS)]
        [InlineData(OperatingSystemKind.Linux)]
        public void Rider_StdioManualCommand_IsUnixShell(OperatingSystemKind os)
        {
            var c = new RiderConfigurator();
            var fields = ReadOnlyFieldTexts(c, SettingsForOs(os));

            fields.ShouldContain(t => t.Contains("mkdir -p .junie/mcp") && t.Contains("printf"));
            fields.ShouldNotContain(t => t.Contains("New-Item -ItemType Directory"));
        }

        private static string[] ReadOnlyFieldTexts(RiderConfigurator c, AgentConfiguratorSettings settings)
            => c.Describe(settings, TransportMethod.stdio).Sections
                .SelectMany(s => s.Items)
                .Where(i => i.Kind == ConfigurationItemKind.ReadOnlyField)
                .Select(i => i.Text)
                .ToArray();
    }
}
