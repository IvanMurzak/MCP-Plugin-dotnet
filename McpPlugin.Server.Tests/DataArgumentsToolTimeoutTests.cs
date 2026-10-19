/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public sealed class DataArgumentsToolTimeoutTests : IDisposable
    {
        // Process-wide environment state: snapshot, clear, restore.
        readonly string? _originalToolTimeout;
        readonly string? _originalPluginTimeout;

        public DataArgumentsToolTimeoutTests()
        {
            _originalToolTimeout = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.ToolTimeout);
            _originalPluginTimeout = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.PluginTimeout);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.ToolTimeout, null);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginTimeout, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.ToolTimeout, _originalToolTimeout);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginTimeout, _originalPluginTimeout);
        }

        [Fact]
        public void Default_IsFiveMinutes_AndIndependentOfPluginTimeout()
        {
            var args = new DataArguments(new[] { "plugin-timeout=2000" });

            args.PluginTimeoutMs.ShouldBe(2000);
            args.ToolTimeoutMs.ShouldBe(Consts.Hub.DefaultToolTimeoutMs);
            Consts.Hub.DefaultToolTimeoutMs.ShouldBe(300_000);
        }

        [Fact]
        public void Cli_SetsIt()
            => new DataArguments(new[] { "tool-timeout=45000" }).ToolTimeoutMs.ShouldBe(45000);

        [Fact]
        public void Env_SetsIt_AndCliWins()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.ToolTimeout, "60000");

            new DataArguments(Array.Empty<string>()).ToolTimeoutMs.ShouldBe(60000);
            new DataArguments(new[] { "tool-timeout=70000" }).ToolTimeoutMs.ShouldBe(70000);
        }

        [Theory]
        [InlineData("tool-timeout=0")]
        [InlineData("tool-timeout=-1")]
        [InlineData("tool-timeout=abc")]
        public void InvalidValue_FallsBackToDefault(string arg)
            => new DataArguments(new[] { arg }).ToolTimeoutMs.ShouldBe(Consts.Hub.DefaultToolTimeoutMs);

        [Fact]
        public void Unset_WithLargerPluginTimeout_KeepsThePluginTimeout()
        {
            // A deployment that raised plugin-timeout to let slow tools finish must not get a shorter wait.
            new DataArguments(new[] { "plugin-timeout=600000" }).ToolTimeoutMs.ShouldBe(600000);
        }

        [Fact]
        public void GetToolTimeoutMs_OnArgumentsWithoutTheInterface_UsesTheLargerOfDefaultAndPluginTimeout()
        {
            Mock.Of<IDataArguments>(a => a.PluginTimeoutMs == 1000).GetToolTimeoutMs().ShouldBe(Consts.Hub.DefaultToolTimeoutMs);
            Mock.Of<IDataArguments>(a => a.PluginTimeoutMs == 900_000).GetToolTimeoutMs().ShouldBe(900_000);
        }
    }
}
