/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// <see cref="ClientOptInHeaders"/> is the ONE definition of how an opt-in header is read, and it
    /// now has two callers that read it at different moments of a session's life
    /// (<see cref="McpSessionTokenMiddleware"/> per request, <c>RunSessionHandler</c> at
    /// <c>initialize</c>). These cases pin the contract those callers share.
    ///
    /// <para>The tri-state is the load-bearing part. ABSENT must be distinguishable from
    /// PRESENT-but-wrong, because the two callers do different things with it: the middleware leaves
    /// the ambient flag untouched on absent, the session handler asserts <c>false</c>. Collapsing to
    /// a plain <c>bool</c> would silently give both callers one behaviour, and nothing above the
    /// transport would notice.</para>
    /// </summary>
    public class ClientOptInHeadersTests
    {
        [Fact]
        public void Read_ReturnsNull_WhenTheHeaderIsAbsent()
        {
            // Not `false`. A caller that must not clobber an existing ambient value can only tell
            // "the client said no" from "the client said nothing" through this null.
            ClientOptInHeaders.Read(new HeaderDictionary(), "X-Anything", "1").ShouldBeNull();
        }

        [Fact]
        public void Read_ReturnsTrue_ForTheExactOptInValue()
        {
            var headers = new HeaderDictionary { ["X-Anything"] = "1" };
            ClientOptInHeaders.Read(headers, "X-Anything", "1").ShouldBe(true);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("true")]
        [InlineData("yes")]
        [InlineData("TRUE")]
        [InlineData("01")]
        [InlineData("1 ")]
        [InlineData(" 1")]
        [InlineData("")]
        public void Read_ReturnsFalse_ForAnyOtherValue(string value)
        {
            // FALSE, not null: the header WAS sent, so a caller that distinguishes absent from
            // wrong must still see "present". An ordinal comparison is what makes every row here
            // fail; a truthiness or case-insensitive test would let several through.
            var headers = new HeaderDictionary { ["X-Anything"] = value };
            ClientOptInHeaders.Read(headers, "X-Anything", "1").ShouldBe(false);
        }

        [Fact]
        public void Read_ReturnsNull_ForANullHeaderDictionary()
        {
            // stdio has no HttpContext at all; the session-scoped caller must not have to guard.
            ClientOptInHeaders.Read(null, "X-Anything", "1").ShouldBeNull();
        }

        [Fact]
        public void TheTwoNamedReaders_AreBoundToTheirOwnHeaderAndValue()
        {
            // Each reader sees ONLY its own header. A copy-paste that pointed both at the same
            // constant would re-merge the two axes at the point they are read — the same defect
            // SkillMetaClientGateTests pins at the flag level, one layer lower.
            var skillOnly = new HeaderDictionary
            {
                [Consts.MCP.Server.Headers.SkillMetaClient] = Consts.MCP.Server.Headers.SkillMetaClientOptInValue
            };
            ClientOptInHeaders.ReadSkillMetaClient(skillOnly).ShouldBe(true);
            ClientOptInHeaders.ReadTrustedInternalClient(skillOnly).ShouldBeNull();

            var trustedOnly = new HeaderDictionary
            {
                [Consts.MCP.Server.Headers.TrustedInternalClient] = Consts.MCP.Server.Headers.TrustedInternalClientOptInValue
            };
            ClientOptInHeaders.ReadTrustedInternalClient(trustedOnly).ShouldBe(true);
            ClientOptInHeaders.ReadSkillMetaClient(trustedOnly).ShouldBeNull();
        }

        [Fact]
        public void TheNamedReaders_ApplyTheSameOrdinalRule()
        {
            var wrong = new HeaderDictionary
            {
                [Consts.MCP.Server.Headers.SkillMetaClient] = "true",
                [Consts.MCP.Server.Headers.TrustedInternalClient] = "true"
            };
            ClientOptInHeaders.ReadSkillMetaClient(wrong).ShouldBe(false);
            ClientOptInHeaders.ReadTrustedInternalClient(wrong).ShouldBe(false);
        }
    }
}
