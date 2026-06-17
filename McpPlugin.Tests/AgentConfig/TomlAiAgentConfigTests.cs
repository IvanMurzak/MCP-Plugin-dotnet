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
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Consts.MCP.Server.TransportMethod;

    /// <summary>Ported from Unity-MCP's <c>TomlAiAgentConfigTests</c> (UIToolkit-free).</summary>
    public class TomlAiAgentConfigTests : AiAgentConfigTestBase
    {
        private TomlAiAgentConfig CreateStdioConfig(string configPath, string bodyPath = "mcp_servers")
        {
            return new TomlAiAgentConfig("Test", configPath, bodyPath)
                .SetProperty("command", ExecutableFullPath.Replace('\\', '/'), requiredForConfiguration: true)
                .SetProperty("args", new[]
                {
                    $"{Consts.MCP.Server.Args.Port}={Port}",
                    $"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}",
                    $"{Consts.MCP.Server.Args.ClientTransportMethod}={TransportMethod.stdio}"
                }, requiredForConfiguration: true)
                .SetPropertyToRemove("url");
        }

        private TomlAiAgentConfig CreateHttpConfig(string configPath, string bodyPath = "mcp_servers")
        {
            return new TomlAiAgentConfig("Test", configPath, bodyPath)
                .SetProperty("url", Host, requiredForConfiguration: true)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");
        }

        [Fact]
        public void Configure_NewFile_CreatesCorrectStructure()
        {
            File.Delete(TempConfigPath);
            CreateStdioConfig(TempConfigPath).Configure().ShouldBeTrue();
            File.Exists(TempConfigPath).ShouldBeTrue();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
            content.ShouldContain("command = ");
            content.ShouldContain("args = [");
        }

        [Fact]
        public void Configure_NewFile_ContainsAllArguments()
        {
            File.Delete(TempConfigPath);
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain($"{Consts.MCP.Server.Args.Port}={Port}");
            content.ShouldContain($"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}");
            content.ShouldContain($"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio");
        }

        [Fact]
        public void Configure_HttpConfig_NewFile_CreatesCorrectStructure()
        {
            File.Delete(TempConfigPath);
            CreateHttpConfig(TempConfigPath).Configure().ShouldBeTrue();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
            content.ShouldContain($"url = \"{Host}\"");
            content.ShouldNotContain("command = ");
            content.ShouldNotContain("args = [");
        }

        [Fact]
        public void Configure_ExistingFile_PreservesOtherSections()
        {
            File.WriteAllText(TempConfigPath, "[other_section]\nkey = \"value\"\n");
            CreateStdioConfig(TempConfigPath).Configure().ShouldBeTrue();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("[other_section]");
            content.ShouldContain("key = \"value\"");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Configure_ExistingSection_MergesProperties()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\ncustom_prop = \"should-stay\"\n");
            CreateStdioConfig(TempConfigPath).Configure().ShouldBeTrue();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("custom_prop = \"should-stay\"");
            content.ShouldNotContain("old-command");
        }

        [Fact]
        public void Configure_ExistingSection_RemovesSpecifiedProperties()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nurl = \"http://old-url\"\n");
            CreateStdioConfig(TempConfigPath).Configure().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldNotContain("url = ");
        }

        [Fact]
        public void Configure_MultipleCalls_DoesNotDuplicate()
        {
            var config = CreateStdioConfig(TempConfigPath);
            config.Configure();
            config.Configure();

            var content = File.ReadAllText(TempConfigPath);
            var sectionHeader = $"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]";
            var firstIndex = content.IndexOf(sectionHeader, System.StringComparison.Ordinal);
            content.IndexOf(sectionHeader, firstIndex + 1, System.StringComparison.Ordinal).ShouldBe(-1);
        }

        [Fact]
        public void Configure_EmptyConfigPath_ReturnsFalse()
        {
            new TomlAiAgentConfig("Test", "", "mcp_servers")
                .SetProperty("command", "some-command", requiredForConfiguration: true)
                .Configure().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_AfterConfigure_ReturnsTrue()
        {
            var config = CreateStdioConfig(TempConfigPath);
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_HttpAfterConfigure_ReturnsTrue()
        {
            var config = CreateHttpConfig(TempConfigPath);
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_EmptyFile_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, "");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_WrongCommand_ReturnsFalse()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"wrong-command\"\nargs = [\"{Consts.MCP.Server.Args.Port}={Port}\",\"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}\",\"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio\"]\n");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_MissingArgs_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"{executable}\"\n");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_HasPropertyToRemove_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            var argsStr = $"\"{Consts.MCP.Server.Args.Port}={Port}\",\"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}\",\"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio\"";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"{executable}\"\nargs = [{argsStr}]\nurl = \"http://some-url\"\n");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_DifferentBodyPath_ReturnsFalse()
        {
            CreateStdioConfig(TempConfigPath, "mcp_servers").Configure();
            CreateStdioConfig(TempConfigPath, "other_path").IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void Configure_CodexLikeConfig_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Codex", TempConfigPath, "mcp_servers")
                .SetProperty("enabled", true, requiredForConfiguration: true)
                .SetProperty("command", ExecutableFullPath.Replace('\\', '/'), requiredForConfiguration: true)
                .SetProperty("args", new[]
                {
                    $"{Consts.MCP.Server.Args.Port}={Port}",
                    $"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}",
                    $"{Consts.MCP.Server.Args.ClientTransportMethod}={TransportMethod.stdio}"
                }, requiredForConfiguration: true)
                .SetProperty("tool_timeout_sec", 300, requiredForConfiguration: false)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("type");

            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void Configure_WithBooleanProperty_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("enabled", true, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void Configure_WithIntegerProperty_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("timeout", 300, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void ExpectedFileContent_CustomBodyPath_UsesCorrectSection()
        {
            var content = CreateStdioConfig(TempConfigPath, "custom_path").ExpectedFileContent;
            content.ShouldContain($"[custom_path.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Configure_WithIntArray_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("ports", new[] { 8080, 8081, 8082 }, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldContain("ports = [8080,8081,8082]");
        }

        [Fact]
        public void Configure_WithBoolArray_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("flags", new[] { true, false, true }, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldContain("flags = [true,false,true]");
        }

        [Fact]
        public void Configure_WithStringArray_IsConfiguredReturnsTrue()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("names", new[] { "alpha", "beta", "gamma" }, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldContain("names = [\"alpha\",\"beta\",\"gamma\"]");
        }

        [Fact]
        public void IsConfigured_ExistingIntArray_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nports = [8080, 8081, 8082]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("ports", new[] { 8080, 8081, 8082 }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_MismatchedIntArray_ReturnsFalse()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nports = [9000, 9001]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("ports", new[] { 8080, 8081, 8082 }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void Configure_NegativeIntArray_HandledCorrectly()
        {
            File.Delete(TempConfigPath);
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("offsets", new[] { -10, 0, 10 }, requiredForConfiguration: true);
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldContain("offsets = [-10,0,10]");
        }

        [Fact]
        public void ExpectedFileContent_PropertiesInAlphabeticalOrder()
        {
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("zebra", "last")
                .SetProperty("alpha", "first")
                .SetProperty("middle", "mid");

            var propLines = config.ExpectedFileContent.Split('\n')
                .Select(line => line.Trim())
                .Where(t => !string.IsNullOrEmpty(t) && !t.StartsWith("[") && t.Contains(" = "))
                .ToList();

            propLines.Count.ShouldBe(3);
            propLines[0].ShouldStartWith("alpha");
            propLines[1].ShouldStartWith("middle");
            propLines[2].ShouldStartWith("zebra");
        }

        [Fact]
        public void Configure_Stdio_RemovesDuplicateByCommand()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\ncommand = \"{executable}\"\nargs = [\"--old-arg\"]\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldNotContain("[mcp_servers.my-custom-name]");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Configure_Http_RemovesDuplicateByServerUrl()
        {
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\nserverUrl = \"{Host}\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .AddIdentityKey("serverUrl")
                .SetProperty("serverUrl", Host, requiredForConfiguration: true)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args")
                .SetPropertyToRemove("url")
                .Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldNotContain("[mcp_servers.my-custom-name]");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void IsDetected_DeprecatedName_ReturnsTrue()
        {
            File.WriteAllText(TempConfigPath, "[mcp_servers.Unity-MCP]\ncommand = \"/some/path\"\n");
            CreateStdioConfig(TempConfigPath).IsDetected().ShouldBeTrue();
        }

        [Fact]
        public void Unconfigure_DeprecatedAndCurrentPresent_RemovesBoth()
        {
            var existingToml = "[mcp_servers.Unity-MCP]\ncommand = \"/old/path\"\n\n" +
                               $"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]\ncommand = \"/some/path\"\n";
            File.WriteAllText(TempConfigPath, existingToml);
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeTrue();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldNotContain("[mcp_servers.Unity-MCP]");
            content.ShouldNotContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Unconfigure_NothingPresent_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, "[mcp_servers.other-server]\ncommand = \"completely-different-command\"\n");
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeFalse();
        }

        [Fact]
        public void Configure_ExistingSection_PreservesFloatValue()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\ntimeout = 1.5\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("timeout = 1.5");
            content.ShouldNotContain("timeout = \"1.5\"");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnInt()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nport = 8080 # default port\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("port = 8080");
            content.ShouldNotContain("# default port");
            content.ShouldNotContain("port = \"");
        }

        [Fact]
        public void IsConfigured_StringArrayWithHashInsideQuotes_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ntags = [\"C#\", \"F#\"] # languages\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("tags", new[] { "C#", "F#" }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_PathComparison_BackslashEqualsForwardSlash()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"C:\\\\Users\\\\test\\\\app.exe\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("command", "C:/Users/test/app.exe", requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_UrlComparison_SchemeCaseInsensitive()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nurl = \"HTTP://LOCALHOST:5000/mcp\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("url", "http://localhost:5000/mcp", requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_ExactComparison_RejectsDifferentPaths()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"C:\\\\Users\\\\test\\\\app.exe\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("command", "C:/Users/test/app.exe", requiredForConfiguration: true)
                .IsConfigured().ShouldBeFalse();
        }

        // ── Cases below ported verbatim from Unity-MCP's TomlAiAgentConfigTests to restore the
        //    full matrix (NUnit Assert → Shouldly; UnityTest coroutine shape → plain [Fact];
        //    test logic and data unchanged). ───────────────────────────────────────────────

        [Fact]
        public void IsConfigured_NonExistentFile_ReturnsFalse()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_config.toml");
            CreateStdioConfig(nonExistentPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void ExpectedFileContent_ContainsCorrectSection()
        {
            var content = CreateStdioConfig(TempConfigPath).ExpectedFileContent;
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
            content.ShouldContain("command = ");
            content.ShouldContain("args = [");
        }

        [Fact]
        public void ExpectedFileContent_HttpConfig_ContainsUrl()
        {
            var content = CreateHttpConfig(TempConfigPath).ExpectedFileContent;
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
            content.ShouldContain($"url = \"{Host}\"");
            content.ShouldNotContain("command = ");
            content.ShouldNotContain("args = [");
        }

        [Fact]
        public void IsConfigured_ExistingBoolArray_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nflags = [true, false, true]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("flags", new[] { true, false, true }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_ExistingStringArray_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nnames = [\"alpha\", \"beta\", \"gamma\"]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("names", new[] { "alpha", "beta", "gamma" }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_MismatchedBoolArray_ReturnsFalse()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nflags = [false, false]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("flags", new[] { true, false, true }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void Configure_ExistingFileWithIntArray_MergesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nports = [1, 2, 3]\ncustom_prop = \"keep\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("ports", new[] { 8080, 8081 }, requiredForConfiguration: true)
                .Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("ports = [8080,8081]");
            content.ShouldContain("custom_prop = \"keep\"");
        }

        [Fact]
        public void Configure_EmptyArray_HandledCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nempty = []\n");
            var config = new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("value", 42, requiredForConfiguration: true);
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_ExistingNegativeIntArray_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\noffsets = [-10, 0, 10]\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("offsets", new[] { -10, 0, 10 }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void Configure_NewFile_PropertiesInAlphabeticalOrder()
        {
            File.Delete(TempConfigPath);
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("zebra", "last")
                .SetProperty("alpha", "first")
                .SetProperty("middle", "mid")
                .Configure();

            var content = File.ReadAllText(TempConfigPath);
            var alphaIdx = content.IndexOf("alpha = ", System.StringComparison.Ordinal);
            var middleIdx = content.IndexOf("middle = ", System.StringComparison.Ordinal);
            var zebraIdx = content.IndexOf("zebra = ", System.StringComparison.Ordinal);

            alphaIdx.ShouldBeGreaterThanOrEqualTo(0);
            middleIdx.ShouldBeGreaterThanOrEqualTo(0);
            zebraIdx.ShouldBeGreaterThanOrEqualTo(0);
            alphaIdx.ShouldBeLessThan(middleIdx);
            middleIdx.ShouldBeLessThan(zebraIdx);
        }

        [Fact]
        public void Configure_ExistingSection_MergedPropertiesInAlphabeticalOrder()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nexisting = \"value\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("zebra", "last")
                .SetProperty("alpha", "first")
                .Configure();

            var content = File.ReadAllText(TempConfigPath);
            var alphaIdx = content.IndexOf("alpha = ", System.StringComparison.Ordinal);
            var existingIdx = content.IndexOf("existing = ", System.StringComparison.Ordinal);
            var zebraIdx = content.IndexOf("zebra = ", System.StringComparison.Ordinal);

            alphaIdx.ShouldBeGreaterThanOrEqualTo(0);
            existingIdx.ShouldBeGreaterThanOrEqualTo(0);
            zebraIdx.ShouldBeGreaterThanOrEqualTo(0);
            alphaIdx.ShouldBeLessThan(existingIdx);
            existingIdx.ShouldBeLessThan(zebraIdx);
        }

        [Fact]
        public void Configure_Http_RemovesDuplicateByUrl()
        {
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\nurl = \"{Host}\"\n");
            CreateHttpConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldNotContain("[mcp_servers.my-custom-name]");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Configure_Http_DefaultIdentityKeys_DoNotRemoveByServerUrl()
        {
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\nserverUrl = \"{Host}\"\n");
            CreateHttpConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("[mcp_servers.my-custom-name]");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void Configure_PreservesUnrelatedServers()
        {
            File.WriteAllText(TempConfigPath, "[mcp_servers.other-server]\ncommand = \"completely-different-command\"\nargs = [\"--some-arg\"]\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("[mcp_servers.other-server]");
            content.ShouldContain($"[mcp_servers.{AiAgentConfig.DefaultMcpServerName}]");
        }

        [Fact]
        public void IsDetected_DuplicateByCommand_ReturnsTrue()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\ncommand = \"{executable}\"\nargs = [\"--old-arg\"]\n");
            CreateStdioConfig(TempConfigPath).IsDetected().ShouldBeTrue();
        }

        [Fact]
        public void IsDetected_DuplicateByUrl_ReturnsTrue()
        {
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\nurl = \"{Host}\"\n");
            CreateHttpConfig(TempConfigPath).IsDetected().ShouldBeTrue();
        }

        [Fact]
        public void Unconfigure_DeprecatedName_RemovesIt()
        {
            File.WriteAllText(TempConfigPath, "[mcp_servers.Unity-MCP]\ncommand = \"/some/path\"\n");
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldNotContain("[mcp_servers.Unity-MCP]");
        }

        [Fact]
        public void Unconfigure_DuplicateByCommand_RemovesIt()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $"[mcp_servers.my-custom-name]\ncommand = \"{executable}\"\nargs = [\"--old-arg\"]\n");
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeTrue();
            File.ReadAllText(TempConfigPath).ShouldNotContain("[mcp_servers.my-custom-name]");
        }

        [Fact]
        public void Configure_ExistingSection_PreservesDateValue()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\ncreated = 2024-01-01\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("created = 2024-01-01");
            content.ShouldNotContain("created = \"2024-01-01\"");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnBool()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nenabled = true # some flag\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("enabled = true");
            content.ShouldNotContain("# some flag");
            content.ShouldNotContain("enabled = \"");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnQuotedString()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nname = \"hello\" # some note\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("name = \"hello\"");
            content.ShouldNotContain("# some note");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnStringArray()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nnames = [\"alpha\", \"beta\"] # some list\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("names = [\"alpha\",\"beta\"]");
            content.ShouldNotContain("# some list");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnIntArray()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nports = [8080, 8081] # default ports\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("ports = [8080,8081]");
            content.ShouldNotContain("# default ports");
        }

        [Fact]
        public void Configure_ExistingSection_InlineCommentOnBoolArray()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"old-command\"\nflags = [true, false] # feature flags\n");
            CreateStdioConfig(TempConfigPath).Configure();

            var content = File.ReadAllText(TempConfigPath);
            content.ShouldContain("flags = [true,false]");
            content.ShouldNotContain("# feature flags");
        }

        [Fact]
        public void IsConfigured_StringArrayWithInlineComment_MatchesCorrectly()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nnames = [\"alpha\", \"beta\"] # a comment\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("names", new[] { "alpha", "beta" }, requiredForConfiguration: true)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_WithInlineComments_ReturnsTrue()
        {
            var config = CreateStdioConfig(TempConfigPath);
            config.Configure();

            var lines = File.ReadAllLines(TempConfigPath);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("command =", System.StringComparison.Ordinal))
                {
                    lines[i] += " # path to executable";
                    break;
                }
            }
            File.WriteAllLines(TempConfigPath, lines);

            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_PathComparison_TrailingSlashIgnored()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\ncommand = \"C:/Users/test/app/\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("command", "C:/Users/test/app", requiredForConfiguration: true, comparison: ValueComparisonMode.Path)
                .IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_UrlComparison_TrailingSlashIgnored()
        {
            var sectionName = $"mcp_servers.{AiAgentConfig.DefaultMcpServerName}";
            File.WriteAllText(TempConfigPath, $"[{sectionName}]\nurl = \"http://localhost:5000/mcp/\"\n");
            new TomlAiAgentConfig("Test", TempConfigPath, "mcp_servers")
                .SetProperty("url", "http://localhost:5000/mcp", requiredForConfiguration: true, comparison: ValueComparisonMode.Url)
                .IsConfigured().ShouldBeTrue();
        }
    }
}
