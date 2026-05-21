/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class DataArgumentsIdleTimeoutTests : IDisposable
    {
        // The Env-var test path mutates process-wide environment state. We snapshot+restore
        // the variable so parallel xUnit runs and downstream tests are not affected.
        private readonly string? _originalEnv;

        public DataArgumentsIdleTimeoutTests()
        {
            _originalEnv = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds, _originalEnv);
        }

        [Fact]
        public void IdleTimeoutSeconds_WhenNoArgs_UsesDefault()
        {
            var args = new DataArguments(Array.Empty<string>());

            args.IdleTimeoutSeconds.ShouldBe(Consts.MCP.Server.DefaultIdleTimeoutSeconds);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromCli_PositiveValue_IsApplied()
        {
            var args = new DataArguments(new[] { "idle-timeout-seconds=900" });

            args.IdleTimeoutSeconds.ShouldBe(900);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromCli_AllowsShortValueForTests()
        {
            // The original hard-coded value was 30 — keep it as a possible value
            // for test scenarios that intentionally exercise idle eviction.
            var args = new DataArguments(new[] { "idle-timeout-seconds=30" });

            args.IdleTimeoutSeconds.ShouldBe(30);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromCli_NonPositive_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "idle-timeout-seconds=0" });

            args.IdleTimeoutSeconds.ShouldBe(Consts.MCP.Server.DefaultIdleTimeoutSeconds);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromCli_Negative_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "idle-timeout-seconds=-5" });

            args.IdleTimeoutSeconds.ShouldBe(Consts.MCP.Server.DefaultIdleTimeoutSeconds);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromCli_NonInteger_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "idle-timeout-seconds=not-a-number" });

            args.IdleTimeoutSeconds.ShouldBe(Consts.MCP.Server.DefaultIdleTimeoutSeconds);
        }

        [Fact]
        public void IdleTimeoutSeconds_FromEnv_PositiveValue_IsApplied()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds, "1200");

            var args = new DataArguments(Array.Empty<string>());

            args.IdleTimeoutSeconds.ShouldBe(1200);
        }

        [Fact]
        public void IdleTimeoutSeconds_CliOverridesEnv()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.IdleTimeoutSeconds, "1200");

            var args = new DataArguments(new[] { "idle-timeout-seconds=300" });

            args.IdleTimeoutSeconds.ShouldBe(300);
        }
    }
}
