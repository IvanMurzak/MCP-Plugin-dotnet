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
using System.Text.Json.Nodes;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Consts.MCP.Server.TransportMethod;

    /// <summary>Ported from Unity-MCP's <c>JsonAiAgentConfigTests</c> (UIToolkit-free).</summary>
    public class JsonAiAgentConfigTests : AiAgentConfigTestBase
    {
        private JsonAiAgentConfig CreateStdioConfig(string configPath, string bodyPath = "mcpServers")
        {
            return new JsonAiAgentConfig("Test", configPath, bodyPath)
                .SetProperty("type", JsonValue.Create("stdio")!, requiredForConfiguration: true)
                .SetProperty("command", JsonValue.Create(ExecutableFullPath.Replace('\\', '/'))!, requiredForConfiguration: true)
                .SetProperty("args", new JsonArray
                {
                    $"{Consts.MCP.Server.Args.Port}={Port}",
                    $"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}",
                    $"{Consts.MCP.Server.Args.ClientTransportMethod}={TransportMethod.stdio}"
                }, requiredForConfiguration: true)
                .SetPropertyToRemove("url");
        }

        private JsonAiAgentConfig CreateHttpConfig(string configPath, string bodyPath = "mcpServers")
        {
            return new JsonAiAgentConfig("Test", configPath, bodyPath)
                .SetProperty("type", JsonValue.Create($"{TransportMethod.streamableHttp}")!, requiredForConfiguration: true)
                .SetProperty("url", JsonValue.Create(Host)!, requiredForConfiguration: true)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args");
        }

        [Fact]
        public void Configure_Stdio_CreatesCorrectFormat()
        {
            var config = CreateStdioConfig(TempConfigPath);

            config.Configure().ShouldBeTrue();
            File.Exists(TempConfigPath).ShouldBeTrue();

            var rootObj = JsonNode.Parse(File.ReadAllText(TempConfigPath))?.AsObject();
            rootObj.ShouldNotBeNull();
            var serverEntry = rootObj!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry.ShouldNotBeNull();
            serverEntry!["command"].ShouldNotBeNull();
            serverEntry["args"].ShouldNotBeNull();
            serverEntry["url"].ShouldBeNull();
        }

        [Fact]
        public void Configure_Stdio_ContainsCorrectArguments()
        {
            var config = CreateStdioConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            var command = serverEntry!["command"]?.GetValue<string>();
            command.ShouldNotBeNull();
            command!.ShouldContain(ExecutableName);

            var args = serverEntry["args"]?.AsArray();
            args.ShouldNotBeNull();
            args!.Any(a => a?.GetValue<string>()?.StartsWith($"{Consts.MCP.Server.Args.Port}=") == true).ShouldBeTrue();
            args.Any(a => a?.GetValue<string>()?.StartsWith($"{Consts.MCP.Server.Args.PluginTimeout}=") == true).ShouldBeTrue();
        }

        [Fact]
        public void Configure_Http_CreatesCorrectFormat()
        {
            var config = CreateHttpConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry.ShouldNotBeNull();
            serverEntry!["url"].ShouldNotBeNull();
            serverEntry["type"]?.GetValue<string>().ShouldBe($"{TransportMethod.streamableHttp}");
            serverEntry["command"].ShouldBeNull();
            serverEntry["args"].ShouldBeNull();
        }

        [Fact]
        public void Configure_Http_ContainsCorrectUrl()
        {
            var config = CreateHttpConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry!["url"]?.GetValue<string>().ShouldBe(Host);
        }

        [Fact]
        public void Configure_Http_NestedBodyPath_CreatesCorrectStructure()
        {
            var bodyPath = $"projects{Consts.MCP.Server.BodyPathDelimiter}myProject{Consts.MCP.Server.BodyPathDelimiter}mcpServers";
            var config = CreateHttpConfig(TempConfigPath, bodyPath);
            config.Configure().ShouldBeTrue();

            var rootObj = JsonNode.Parse(File.ReadAllText(TempConfigPath))?.AsObject();
            var serverEntry = rootObj!["projects"]?["myProject"]?["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry!["url"].ShouldNotBeNull();
            serverEntry["type"]?.GetValue<string>().ShouldBe($"{TransportMethod.streamableHttp}");
        }

        [Fact]
        public void Configure_SwitchFromStdioToHttp_RemovesStdioProperties()
        {
            CreateStdioConfig(TempConfigPath).Configure();
            CreateHttpConfig(TempConfigPath).Configure().ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry!["url"].ShouldNotBeNull();
            serverEntry["command"].ShouldBeNull();
            serverEntry["args"].ShouldBeNull();
        }

        [Fact]
        public void Configure_SwitchFromHttpToStdio_RemovesHttpProperties()
        {
            CreateHttpConfig(TempConfigPath).Configure();
            CreateStdioConfig(TempConfigPath).Configure().ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry!["command"].ShouldNotBeNull();
            serverEntry["args"].ShouldNotBeNull();
            serverEntry["url"].ShouldBeNull();
        }

        [Fact]
        public void Configure_SwitchTransport_PreservesOtherServers()
        {
            File.WriteAllText(TempConfigPath, @"{ ""mcpServers"": { ""otherServer"": { ""command"": ""other-command"", ""args"": [""--other-arg""] } } }");
            CreateStdioConfig(TempConfigPath).Configure();
            CreateHttpConfig(TempConfigPath).Configure().ShouldBeTrue();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["otherServer"].ShouldNotBeNull();
            mcpServers[AiAgentConfig.DefaultMcpServerName]?["url"].ShouldNotBeNull();
        }

        [Fact]
        public void IsConfigured_Stdio_ValidConfig_ReturnsTrue()
        {
            var config = CreateStdioConfig(TempConfigPath);
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_Stdio_WithUrlProperty_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": ""{executable}"", ""args"": [""{Consts.MCP.Server.Args.Port}={Port}"", ""{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}""], ""url"": ""http://localhost:50000/mcp"" }} }} }}");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_Stdio_MissingCommand_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""args"": [""{Consts.MCP.Server.Args.Port}={Port}""] }} }} }}");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_Stdio_WrongPort_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": ""{executable}"", ""args"": [""{Consts.MCP.Server.Args.Port}=99999"", ""{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}""] }} }} }}");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_Http_ValidConfig_ReturnsTrue()
        {
            var config = CreateHttpConfig(TempConfigPath);
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_Http_WithCommandProperty_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""url"": ""{Host}"", ""type"": ""{TransportMethod.streamableHttp}"", ""command"": ""some-command"" }} }} }}");
            CreateHttpConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_Http_MissingUrl_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""type"": ""{TransportMethod.streamableHttp}"" }} }} }}");
            CreateHttpConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_Http_WrongType_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""url"": ""{Host}"", ""type"": ""stdio"" }} }} }}");
            CreateHttpConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_StdioTransport_WithHttpConfig_ReturnsFalse()
        {
            CreateHttpConfig(TempConfigPath).Configure();
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_HttpTransport_WithStdioConfig_ReturnsFalse()
        {
            CreateStdioConfig(TempConfigPath).Configure();
            CreateHttpConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void ExpectedFileContent_Stdio_ReturnsCorrectFormat()
        {
            var content = CreateStdioConfig(TempConfigPath).ExpectedFileContent;
            var serverEntry = JsonNode.Parse(content)!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry!["command"].ShouldNotBeNull();
            serverEntry["args"].ShouldNotBeNull();
            serverEntry["url"].ShouldBeNull();
        }

        [Fact]
        public void IsConfigured_NonExistentFile_ReturnsFalse()
        {
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_config_12345.json");
            CreateStdioConfig(nonExistentPath).IsConfigured().ShouldBeFalse();
            CreateHttpConfig(nonExistentPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_EmptyFile_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, "");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
            CreateHttpConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void Configure_EmptyConfigPath_ReturnsFalse()
        {
            CreateStdioConfig("").Configure().ShouldBeFalse();
            CreateHttpConfig("").Configure().ShouldBeFalse();
        }

        [Fact]
        public void Configure_MultipleCalls_SameTransport_UpdatesConfiguration()
        {
            var config = CreateHttpConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!.Count(kv => !string.IsNullOrEmpty(kv.Value?["url"]?.GetValue<string>())).ShouldBe(1);
        }

        [Fact]
        public void IsConfigured_OtherServerMatches_ButDefaultMissing_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""otherServer"": {{ ""type"": ""stdio"", ""command"": ""{executable}"", ""args"": [""{Consts.MCP.Server.Args.Port}={Port}"", ""{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}"", ""{Consts.MCP.Server.Args.ClientTransportMethod}=stdio""] }} }} }}");
            CreateStdioConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void ExpectedFileContent_PropertiesInAlphabeticalOrder()
        {
            var config = new JsonAiAgentConfig("Test", TempConfigPath, "mcpServers")
                .SetProperty("zebra", JsonValue.Create("last")!)
                .SetProperty("alpha", JsonValue.Create("first")!)
                .SetProperty("middle", JsonValue.Create("mid")!);

            var serverEntry = JsonNode.Parse(config.ExpectedFileContent)!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            var keys = serverEntry!.Select(kv => kv.Key).ToList();
            keys[0].ShouldBe("alpha");
            keys[1].ShouldBe("middle");
            keys[2].ShouldBe("zebra");
        }

        [Fact]
        public void Configure_Stdio_RemovesDuplicateByCommand()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""my-custom-name"": {{ ""type"": ""stdio"", ""command"": ""{executable}"", ""args"": [""--old-arg""] }} }} }}");
            CreateStdioConfig(TempConfigPath).Configure();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["my-custom-name"].ShouldBeNull();
            mcpServers[AiAgentConfig.DefaultMcpServerName].ShouldNotBeNull();
        }

        [Fact]
        public void Configure_Http_RemovesDuplicateByServerUrl()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""my-custom-name"": {{ ""serverUrl"": ""{Host}"" }} }} }}");
            var config = new JsonAiAgentConfig("Test", TempConfigPath, "mcpServers")
                .AddIdentityKey("serverUrl")
                .SetProperty("serverUrl", JsonValue.Create(Host)!, requiredForConfiguration: true)
                .SetPropertyToRemove("command")
                .SetPropertyToRemove("args")
                .SetPropertyToRemove("url");
            config.Configure();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["my-custom-name"].ShouldBeNull();
            mcpServers[AiAgentConfig.DefaultMcpServerName].ShouldNotBeNull();
        }

        [Fact]
        public void Configure_Http_DefaultIdentityKeys_DoNotRemoveByServerUrl()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""my-custom-name"": {{ ""serverUrl"": ""{Host}"" }} }} }}");
            CreateHttpConfig(TempConfigPath).Configure();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["my-custom-name"].ShouldNotBeNull();
            mcpServers[AiAgentConfig.DefaultMcpServerName].ShouldNotBeNull();
        }

        [Fact]
        public void IsDetected_DeprecatedName_ReturnsTrue()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""Unity-MCP"": {{ ""type"": ""stdio"", ""command"": ""/some/path"" }} }} }}");
            CreateStdioConfig(TempConfigPath).IsDetected().ShouldBeTrue();
        }

        [Fact]
        public void Unconfigure_DeprecatedAndCurrentPresent_RemovesBoth()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""Unity-MCP"": {{ ""command"": ""/old/path"" }}, ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": ""/some/path"" }} }} }}");
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeTrue();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["Unity-MCP"].ShouldBeNull();
            mcpServers[AiAgentConfig.DefaultMcpServerName].ShouldBeNull();
        }

        [Fact]
        public void Unconfigure_NothingPresent_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""other-server"": {{ ""command"": ""completely-different-command"" }} }} }}");
            CreateStdioConfig(TempConfigPath).Unconfigure().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_PathComparison_BackslashEqualsForwardSlash()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": ""C:\\Users\\test\\app.exe"" }} }} }}");
            var config = new JsonAiAgentConfig("Test", TempConfigPath, "mcpServers")
                .SetProperty("command", JsonValue.Create("C:/Users/test/app.exe")!, requiredForConfiguration: true, comparison: ValueComparisonMode.Path);
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_UrlComparison_SchemeCaseInsensitive()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""url"": ""HTTP://LOCALHOST:5000/mcp"" }} }} }}");
            var config = new JsonAiAgentConfig("Test", TempConfigPath, "mcpServers")
                .SetProperty("url", JsonValue.Create("http://localhost:5000/mcp")!, requiredForConfiguration: true, comparison: ValueComparisonMode.Url);
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_ExactComparison_RejectsDifferentPaths()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": ""C:\\Users\\test\\app.exe"" }} }} }}");
            var config = new JsonAiAgentConfig("Test", TempConfigPath, "mcpServers")
                .SetProperty("command", JsonValue.Create("C:/Users/test/app.exe")!, requiredForConfiguration: true);
            config.IsConfigured().ShouldBeFalse();
        }
    }
}
