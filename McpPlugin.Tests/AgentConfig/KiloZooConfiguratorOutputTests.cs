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
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    using TransportMethod = Consts.MCP.Server.TransportMethod;

    /// <summary>
    /// Output-shape parity tests for Kilo Code and Zoo Code. These configurators must reproduce
    /// Unity-MCP's exact config: <c>disabled = false</c> on BOTH transports and HTTP
    /// <c>type = "streamable-http"</c> (Unity KiloCodeConfigurator.cs / ZooCodeConfigurator.cs).
    /// Their absence is why the regression slipped (no output tests existed before).
    /// </summary>
    public class KiloZooConfiguratorOutputTests
    {
        private static AgentConfiguratorSettings Settings(string root) => new(
            operatingSystem: OperatingSystemKind.Windows,
            projectRootPath: root,
            executableFullPath: "C:/Tools/ai-game-developer-mcp-server.exe",
            port: 50000,
            timeoutMs: 30000,
            host: "http://localhost:50000/mcp");

        [Theory]
        [InlineData("kilo-code")]
        [InlineData("zoo-code")]
        public void StdioConfig_HasTypeStdio_DisabledFalse(string agentId)
        {
            var configurator = AiAgentConfiguratorRegistry.GetByAgentId(agentId)!;
            var content = configurator.GetStdioConfig(Settings(Path.GetTempPath())).ExpectedFileContent;

            content.ShouldContain("\"type\": \"stdio\"");
            content.ShouldContain("\"disabled\": false");
        }

        [Theory]
        [InlineData("kilo-code")]
        [InlineData("zoo-code")]
        public void HttpConfig_HasStreamableHttpType_DisabledFalse(string agentId)
        {
            var configurator = AiAgentConfiguratorRegistry.GetByAgentId(agentId)!;
            var content = configurator.GetHttpConfig(Settings(Path.GetTempPath())).ExpectedFileContent;

            content.ShouldContain("\"type\": \"streamable-http\"");
            content.ShouldNotContain("\"type\": \"http\"");
            content.ShouldContain("\"disabled\": false");
        }

        [Theory]
        [InlineData("kilo-code")]
        [InlineData("zoo-code")]
        public void Configure_WritesDisabledFalseAndStreamableHttp_AndIsConfigured(string agentId)
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var configurator = AiAgentConfiguratorRegistry.GetByAgentId(agentId)!;
                var settings = Settings(root);

                var stdio = configurator.GetStdioConfig(settings);
                stdio.Configure().ShouldBeTrue();
                configurator.IsConfigured(settings, TransportMethod.stdio).ShouldBeTrue();

                var http = configurator.GetHttpConfig(settings);
                http.Configure().ShouldBeTrue();
                configurator.IsConfigured(settings, TransportMethod.streamableHttp).ShouldBeTrue();

                var written = File.ReadAllText(http.ConfigPath);
                written.ShouldContain("\"disabled\": false");
                written.ShouldContain("\"streamable-http\"");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
