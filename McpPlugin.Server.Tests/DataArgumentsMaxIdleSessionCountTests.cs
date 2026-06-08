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
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class DataArgumentsMaxIdleSessionCountTests : IDisposable
    {
        // The Env-var test path mutates process-wide environment state. We snapshot+restore
        // the variable so parallel xUnit runs and downstream tests are not affected.
        private readonly string? _originalEnv;

        public DataArgumentsMaxIdleSessionCountTests()
        {
            _originalEnv = Environment.GetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount);
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount, _originalEnv);
        }

        [Fact]
        public void MaxIdleSessionCount_WhenNoArgs_UsesDefault()
        {
            var args = new DataArguments(Array.Empty<string>());

            args.MaxIdleSessionCount.ShouldBe(Consts.MCP.Server.DefaultMaxIdleSessionCount);
        }

        [Fact]
        public void MaxIdleSessionCount_FromCli_PositiveValue_IsApplied()
        {
            var args = new DataArguments(new[] { "max-idle-session-count=256" });

            args.MaxIdleSessionCount.ShouldBe(256);
        }

        [Fact]
        public void MaxIdleSessionCount_FromCli_NonPositive_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "max-idle-session-count=0" });

            args.MaxIdleSessionCount.ShouldBe(Consts.MCP.Server.DefaultMaxIdleSessionCount);
        }

        [Fact]
        public void MaxIdleSessionCount_FromCli_Negative_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "max-idle-session-count=-10" });

            args.MaxIdleSessionCount.ShouldBe(Consts.MCP.Server.DefaultMaxIdleSessionCount);
        }

        [Fact]
        public void MaxIdleSessionCount_FromCli_NonInteger_FallsBackToDefault()
        {
            var args = new DataArguments(new[] { "max-idle-session-count=not-a-number" });

            args.MaxIdleSessionCount.ShouldBe(Consts.MCP.Server.DefaultMaxIdleSessionCount);
        }

        [Fact]
        public void MaxIdleSessionCount_FromEnv_PositiveValue_IsApplied()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount, "2048");

            var args = new DataArguments(Array.Empty<string>());

            args.MaxIdleSessionCount.ShouldBe(2048);
        }

        [Fact]
        public void MaxIdleSessionCount_CliOverridesEnv()
        {
            Environment.SetEnvironmentVariable(Consts.MCP.Server.Env.MaxIdleSessionCount, "2048");

            var args = new DataArguments(new[] { "max-idle-session-count=512" });

            args.MaxIdleSessionCount.ShouldBe(512);
        }
    }
}
