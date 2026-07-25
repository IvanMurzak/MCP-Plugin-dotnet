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
        [InlineData("/p/aabbccdd/api/tools", "aabbccdd")]        // pinned REST tool route
        [InlineData("/p/aabbccdd/api/system-tools/ping", "aabbccdd")]
        [InlineData("/p/11112222/p/33334444", "33334444")]        // last wins (config URL suffix)
        public void TryExtractProjectPin_WellFormedPin_IsCaptured(string path, string expected)
        {
            McpSessionTokenMiddleware.TryExtractProjectPin(path, out var pin).ShouldBe(ProjectPinParse.Valid);
            pin.ShouldBe(expected);
        }

        [Theory]
        [InlineData("/mcp")]                                      // no /p/ marker
        [InlineData("/api/tools")]                                // the genuinely-unpinned REST surface
        [InlineData("/api/tools/p")]                              // trailing "p" is not a marker (nothing follows)
        [InlineData(null)]
        [InlineData("")]
        public void TryExtractProjectPin_NoMarker_IsAbsentNotMalformed(string? path)
        {
            // ABSENT ≠ MALFORMED: an unpinned request is a supported mode and must NOT be rejected.
            McpSessionTokenMiddleware.TryExtractProjectPin(path, out var pin).ShouldBe(ProjectPinParse.Absent);
            pin.ShouldBeNull();
        }

        [Theory]
        [InlineData("/mcp/p/not-a-pin!")]                         // non-hex
        [InlineData("/p/zzzz/api/tools")]                         // non-hex on a pinned REST route
        [InlineData("/mcp/p/")]                                   // marker with an empty value
        [InlineData("/p/aabbccdd0011223344556677889900aabbccdd0011223344556677889900aabbc")] // 65 hex chars (over the 64 cap)
        [InlineData("/p/11112222/p/nothex")]                      // a later bad marker never inherits an earlier good pin
        public void TryExtractProjectPin_PresentButUnparseable_IsMalformed(string path)
        {
            // Previously these all returned null — indistinguishable from "unpinned" — which silently
            // re-enabled MRU fallthrough. They must now be reported as malformed so the caller rejects.
            McpSessionTokenMiddleware.TryExtractProjectPin(path, out var pin).ShouldBe(ProjectPinParse.Malformed);
            pin.ShouldBeNull();
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
