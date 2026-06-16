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

    /// <summary>Ported from Unity-MCP's <c>JsonAiAgentConfigCommandArrayTests</c> (UIToolkit-free).</summary>
    public class JsonAiAgentConfigCommandArrayTests : AiAgentConfigTestBase
    {
        private JsonAiAgentConfig CreateCommandArrayConfig(string configPath, string bodyPath = "mcpServers")
        {
            return new JsonAiAgentConfig("Test", configPath, bodyPath)
                .SetProperty("type", JsonValue.Create("local")!, requiredForConfiguration: true)
                .SetProperty("enabled", JsonValue.Create(true)!, requiredForConfiguration: true)
                .SetProperty("command", new JsonArray
                {
                    ExecutableFullPath.Replace('\\', '/'),
                    $"{Consts.MCP.Server.Args.Port}={Port}",
                    $"{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}",
                    $"{Consts.MCP.Server.Args.ClientTransportMethod}={TransportMethod.stdio}"
                }, requiredForConfiguration: true)
                .SetPropertyToRemove("url")
                .SetPropertyToRemove("args");
        }

        [Fact]
        public void Configure_SimpleBodyPath_CreatesCommandArrayFormat()
        {
            var config = CreateCommandArrayConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();
            File.Exists(TempConfigPath).ShouldBeTrue();

            var serverEntry = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?.AsObject();
            serverEntry.ShouldNotBeNull();
            serverEntry!["type"]?.GetValue<string>().ShouldBe("local");
            serverEntry["enabled"]?.GetValue<bool>().ShouldBe(true);

            var commandArray = serverEntry["command"]?.AsArray();
            commandArray.ShouldNotBeNull();
            commandArray![0]?.GetValue<string>().ShouldContain(ExecutableName);
        }

        [Fact]
        public void Configure_CommandArrayContainsAllRequiredArguments()
        {
            var config = CreateCommandArrayConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();

            var commandArray = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?[AiAgentConfig.DefaultMcpServerName]?["command"]?.AsArray();
            commandArray.ShouldNotBeNull();

            var argStrings = commandArray!.Skip(1).Select(a => a?.GetValue<string>() ?? string.Empty).ToList();
            argStrings.Any(a => a.StartsWith($"{Consts.MCP.Server.Args.Port}=")).ShouldBeTrue();
            argStrings.Any(a => a.StartsWith($"{Consts.MCP.Server.Args.PluginTimeout}=")).ShouldBeTrue();
            argStrings.Any(a => a.Contains($"{Consts.MCP.Server.Args.ClientTransportMethod}=stdio")).ShouldBeTrue();
        }

        [Fact]
        public void Configure_DeepNestedBodyPath_CreatesFullStructure()
        {
            var d = Consts.MCP.Server.BodyPathDelimiter;
            var bodyPath = $"level1{d}level2{d}level3{d}mcpServers";
            var config = CreateCommandArrayConfig(TempConfigPath, bodyPath);
            config.Configure().ShouldBeTrue();

            var rootObj = JsonNode.Parse(File.ReadAllText(TempConfigPath))?.AsObject();
            var mcpServers = rootObj!["level1"]?["level2"]?["level3"]?["mcpServers"]?.AsObject();
            mcpServers![AiAgentConfig.DefaultMcpServerName]?["command"].ShouldNotBeNull();
        }

        [Fact]
        public void Configure_ExistingFileSimpleStructure_PreservesContent()
        {
            File.WriteAllText(TempConfigPath, @"{ ""otherProperty"": ""shouldBePreserved"", ""mcpServers"": { ""existingServer"": { ""command"": [""other-command"", ""--arg1""], ""type"": ""local"" } } }");
            CreateCommandArrayConfig(TempConfigPath).Configure().ShouldBeTrue();

            var rootObj = JsonNode.Parse(File.ReadAllText(TempConfigPath))?.AsObject();
            rootObj!["otherProperty"]?.GetValue<string>().ShouldBe("shouldBePreserved");
            var mcpServers = rootObj["mcpServers"]?.AsObject();
            mcpServers!["existingServer"].ShouldNotBeNull();
            mcpServers.Count.ShouldBeGreaterThan(1);
        }

        [Fact]
        public void Configure_ExistingFileWithDuplicateCommand_ReplacesEntry()
        {
            var duplicateCommand = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""Unity-MCP-Duplicate"": {{ ""command"": [""{duplicateCommand}"", ""--old-port=9999""], ""type"": ""local"", ""enabled"": true }}, ""otherServer"": {{ ""command"": [""other-command"", ""--other-arg""] }} }} }}");
            CreateCommandArrayConfig(TempConfigPath).Configure().ShouldBeTrue();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!["otherServer"].ShouldNotBeNull();

            var commandArray = mcpServers[AiAgentConfig.DefaultMcpServerName]?["command"]?.AsArray();
            commandArray![0]?.GetValue<string>().ShouldBe(duplicateCommand);
            commandArray.ToString().ShouldContain($"{Consts.MCP.Server.Args.Port}={Port}");
        }

        [Fact]
        public void Configure_InvalidJsonFile_ReplacesWithNewConfig()
        {
            File.WriteAllText(TempConfigPath, "{ invalid json }");
            CreateCommandArrayConfig(TempConfigPath).Configure().ShouldBeTrue();
            JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"].ShouldNotBeNull();
        }

        [Fact]
        public void IsConfigured_NestedBodyPath_DetectsCorrectly()
        {
            var d = Consts.MCP.Server.BodyPathDelimiter;
            var config = CreateCommandArrayConfig(TempConfigPath, $"projects{d}myProject{d}mcpServers");
            config.Configure();
            config.IsConfigured().ShouldBeTrue();
        }

        [Fact]
        public void IsConfigured_WrongType_ReturnsFalse()
        {
            var executable = ExecutableFullPath.Replace('\\', '/');
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": [""{executable}"", ""{Consts.MCP.Server.Args.Port}={Port}"", ""{Consts.MCP.Server.Args.PluginTimeout}={TimeoutMs}"", ""{Consts.MCP.Server.Args.ClientTransportMethod}=stdio""], ""type"": ""wrong-type"", ""enabled"": true }} }} }}");
            CreateCommandArrayConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void IsConfigured_WrongCommandArray_ReturnsFalse()
        {
            File.WriteAllText(TempConfigPath, $@"{{ ""mcpServers"": {{ ""{AiAgentConfig.DefaultMcpServerName}"": {{ ""command"": [""wrong-executable"", ""{Consts.MCP.Server.Args.Port}={Port}""], ""type"": ""local"", ""enabled"": true }} }} }}");
            CreateCommandArrayConfig(TempConfigPath).IsConfigured().ShouldBeFalse();
        }

        [Fact]
        public void ExpectedFileContent_NestedBodyPath_ReturnsCorrectNestedStructure()
        {
            var d = Consts.MCP.Server.BodyPathDelimiter;
            var content = CreateCommandArrayConfig(TempConfigPath, $"level1{d}level2{d}mcpServers").ExpectedFileContent;
            var rootObj = JsonNode.Parse(content)?.AsObject();
            rootObj!["level1"]?["level2"]?["mcpServers"].ShouldNotBeNull();
        }

        [Fact]
        public void Configure_MultipleCalls_UpdatesConfiguration()
        {
            var config = CreateCommandArrayConfig(TempConfigPath);
            config.Configure().ShouldBeTrue();
            config.Configure().ShouldBeTrue();
            config.IsConfigured().ShouldBeTrue();

            var mcpServers = JsonNode.Parse(File.ReadAllText(TempConfigPath))!["mcpServers"]?.AsObject();
            mcpServers!.Count.ShouldBe(1);
            mcpServers[AiAgentConfig.DefaultMcpServerName].ShouldNotBeNull();
        }
    }
}
