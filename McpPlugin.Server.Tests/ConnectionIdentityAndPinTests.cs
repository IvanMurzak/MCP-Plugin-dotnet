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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Unit tests for the identity + project-pin capture surfaces of the account pairing plane
    /// (mcp-authorize b3): <see cref="ConnectionIdentity"/> role derivation, URL <c>/p/&lt;pin&gt;</c>
    /// extraction, and the stdio <c>project=&lt;pin&gt;</c> argument.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ConnectionIdentityAndPinTests
    {
        // ─────────────────────────── ConnectionIdentity ───────────────────────────

        [Theory]
        [InlineData("mcp:agent", ConnectionIdentity.RoleAgent)]
        [InlineData("mcp:plugin", ConnectionIdentity.RolePlugin)]
        [InlineData("openid mcp:agent profile", ConnectionIdentity.RoleAgent)]
        [InlineData("mcp:agent mcp:plugin", ConnectionIdentity.RolePlugin)] // plugin scope wins
        [InlineData("openid profile", ConnectionIdentity.RoleUnknown)]
        [InlineData("", ConnectionIdentity.RoleUnknown)]
        [InlineData(null, ConnectionIdentity.RoleUnknown)]
        public void RoleFromScope_MapsScopeToRole(string? scope, string expected)
        {
            ConnectionIdentity.RoleFromScope(scope).ShouldBe(expected);
        }

        [Fact]
        public void Create_WithSubject_BuildsIdentity()
        {
            var id = ConnectionIdentity.Create("user-123", "mcp:agent", clientId: "cli-1");
            id.ShouldNotBeNull();
            id!.AccountId.ShouldBe("user-123");
            id.Role.ShouldBe(ConnectionIdentity.RoleAgent);
            id.IsAgent.ShouldBeTrue();
            id.IsPlugin.ShouldBeFalse();
            id.ClientId.ShouldBe("cli-1");
        }

        [Fact]
        public void Create_WithoutSubject_ReturnsNull()
        {
            ConnectionIdentity.Create(null, "mcp:agent").ShouldBeNull();
            ConnectionIdentity.Create("", "mcp:agent").ShouldBeNull();
        }

        [Fact]
        public void Ctor_EmptyAccountId_Throws()
        {
            Should.Throw<ArgumentException>(() => new ConnectionIdentity("", ConnectionIdentity.RoleAgent));
        }

        // ─────────────────────────── URL /p/<pin> extraction ───────────────────────────

        [Theory]
        [InlineData("/mcp/p/aabbccdd", "aabbccdd")]
        [InlineData("/p/aabbccdd", "aabbccdd")]
        [InlineData("/mcp/p/AABBCCDD", "aabbccdd")]              // lowercased
        [InlineData("/mcp", null)]                                // no /p/ segment
        [InlineData("/mcp/p/", null)]                             // empty pin
        [InlineData("/mcp/p/not-a-pin!", null)]                   // non-hex ignored
        [InlineData("/p/11112222/p/33334444", "33334444")]        // last wins (config URL suffix)
        [InlineData(null, null)]
        [InlineData("", null)]
        public void TryExtractProjectPin_ParsesTrailingPinSegment(string? path, string? expected)
        {
            McpSessionTokenMiddleware.TryExtractProjectPin(path).ShouldBe(expected);
        }

        // ─────────────────────────── stdio project=<pin> arg ───────────────────────────

        [Fact]
        public void DataArguments_ParsesProjectPin_Lowercased()
        {
            new DataArguments(new[] { "project=AABBCCDD" }).ProjectPin.ShouldBe("aabbccdd");
        }

        [Fact]
        public void DataArguments_NoProjectArg_PinIsNull()
        {
            new DataArguments(new[] { "port=8080" }).ProjectPin.ShouldBeNull();
        }
    }
}
