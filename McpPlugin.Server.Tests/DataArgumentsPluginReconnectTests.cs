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
using System;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public sealed class DataArgumentsPluginReconnectTests : IDisposable
    {
        // Process-wide environment state: snapshot, clear, restore.
        readonly string? _originalGrace;
        readonly string? _originalWait;

        public DataArgumentsPluginReconnectTests()
        {
            _originalGrace = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectGraceSeconds);
            _originalWait = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectWaitSeconds);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectGraceSeconds, null);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectWaitSeconds, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectGraceSeconds, _originalGrace);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectWaitSeconds, _originalWait);
        }

        [Fact]
        public void Defaults_AreFiveMinutesOfGrace_AndAMinuteOfWait()
        {
            var args = new DataArguments(Array.Empty<string>());

            args.GetPluginReconnectGrace().ShouldBe(TimeSpan.FromMinutes(5));
            args.GetPluginReconnectWait().ShouldBe(TimeSpan.FromMinutes(1));
        }

        [Fact]
        public void Cli_SetsThem_AndZeroGraceDisables()
        {
            var args = new DataArguments(new[] { "plugin-reconnect-grace-seconds=0", "plugin-reconnect-wait-seconds=15" });

            args.GetPluginReconnectGrace().ShouldBe(TimeSpan.Zero);
            args.GetPluginReconnectWait().ShouldBe(TimeSpan.FromSeconds(15));
        }

        [Fact]
        public void Env_SetsThem_AndCliWins()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectGraceSeconds, "120");
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.PluginReconnectWaitSeconds, "20");

            var fromEnv = new DataArguments(Array.Empty<string>());
            fromEnv.PluginReconnectGraceSeconds.ShouldBe(120);
            fromEnv.PluginReconnectWaitSeconds.ShouldBe(20);
            new DataArguments(new[] { "plugin-reconnect-grace-seconds=30" }).PluginReconnectGraceSeconds.ShouldBe(30);
        }

        [Theory]
        [InlineData("plugin-reconnect-grace-seconds=-1")]
        [InlineData("plugin-reconnect-grace-seconds=abc")]
        public void InvalidGrace_KeepsTheDefault(string arg)
            => new DataArguments(new[] { arg }).PluginReconnectGraceSeconds.ShouldBe(Consts.MCP.Server.DefaultPluginReconnectGraceSeconds);

        [Theory]
        [InlineData("plugin-reconnect-wait-seconds=0")]
        [InlineData("plugin-reconnect-wait-seconds=abc")]
        public void InvalidWait_KeepsTheDefault(string arg)
            => new DataArguments(new[] { arg }).PluginReconnectWaitSeconds.ShouldBe(Consts.MCP.Server.DefaultPluginReconnectWaitSeconds);

        [Fact]
        public void ArgumentsWithoutTheInterface_UseTheDefaults()
        {
            var args = Mock.Of<IDataArguments>();

            args.GetPluginReconnectGrace().ShouldBe(TimeSpan.FromSeconds(Consts.MCP.Server.DefaultPluginReconnectGraceSeconds));
            args.GetPluginReconnectWait().ShouldBe(TimeSpan.FromSeconds(Consts.MCP.Server.DefaultPluginReconnectWaitSeconds));
        }
    }
}
