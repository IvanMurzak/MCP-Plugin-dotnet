/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System.IO;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The configurator's per-mode HTTP/STDIO auth flags (mcp-authorize g6). Token mode must inject the
    /// HTTP <c>Authorization: Bearer &lt;secret&gt;</c> header (HTTP only — stdio stays credential-free per
    /// b6); none and oauth stay URL-only (oauth authorizes natively, none is anonymous).
    /// </summary>
    public class AgentConfiguratorSettingsAuthTests
    {
        static AgentConfiguratorSettings Settings(
            Consts.MCP.Server.AuthOption authOption,
            ConnectionMode connectionMode = ConnectionMode.Local)
            => new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: Path.GetTempPath(),
                executableFullPath: "C:/Tools/srv.exe",
                port: 8080,
                timeoutMs: 30000,
                host: "http://localhost:8080/mcp",
                token: "secret-token",
                connectionMode: connectionMode,
                authOption: authOption);

        [Theory]
        [InlineData(Consts.MCP.Server.AuthOption.token, true)]
        [InlineData(Consts.MCP.Server.AuthOption.required, true)] // deprecated back-compat alias
        [InlineData(Consts.MCP.Server.AuthOption.none, false)]
        [InlineData(Consts.MCP.Server.AuthOption.oauth, false)]
        public void IsHttpAuthRequired_PerMode(Consts.MCP.Server.AuthOption mode, bool expected)
        {
            Settings(mode).IsHttpAuthRequired.ShouldBe(expected);
        }

        [Fact]
        public void IsHttpAuthRequired_Cloud_AlwaysTrue()
        {
            // Cloud enforces auth regardless of the local auth option (even none).
            Settings(Consts.MCP.Server.AuthOption.none, ConnectionMode.Cloud).IsHttpAuthRequired.ShouldBeTrue();
        }

        [Theory]
        [InlineData(Consts.MCP.Server.AuthOption.required, true)]
        [InlineData(Consts.MCP.Server.AuthOption.token, false)] // token mode is HTTP-only; stdio credential-free
        [InlineData(Consts.MCP.Server.AuthOption.none, false)]
        [InlineData(Consts.MCP.Server.AuthOption.oauth, false)]
        public void IsStdioAuthRequired_PerMode(Consts.MCP.Server.AuthOption mode, bool expected)
        {
            Settings(mode).IsStdioAuthRequired.ShouldBe(expected);
        }
    }
}
