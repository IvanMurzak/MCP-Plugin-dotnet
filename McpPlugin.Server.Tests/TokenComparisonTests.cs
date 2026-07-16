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
using com.IvanMurzak.McpPlugin.Server.Auth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The offline <c>token</c> mode's constant-time secret comparison (mcp-authorize g6). Asserts
    /// correctness across the cases a timing-leaky <c>string.Equals</c> would distinguish by early exit:
    /// prefix-sharing values and different-length values both fail, matching values succeed.
    /// </summary>
    public class TokenComparisonTests
    {
        [Fact]
        public void Equal_Tokens_Match()
        {
            TokenComparison.FixedTimeEquals("shared-secret-abc", "shared-secret-abc").ShouldBeTrue();
        }

        [Theory]
        [InlineData("shared-secret-abc", "shared-secret-abd")] // differ in last char
        [InlineData("shared-secret-abc", "shared-secret-abc-longer")] // shared prefix, different length
        [InlineData("shared-secret-abc", "x")] // shorter, no shared prefix
        [InlineData("shared-secret-abc", "Shared-Secret-Abc")] // case differs (case-sensitive)
        public void Mismatched_Tokens_DoNotMatch(string presented, string expected)
        {
            TokenComparison.FixedTimeEquals(presented, expected).ShouldBeFalse();
        }

        [Theory]
        [InlineData(null, "secret")]
        [InlineData("secret", null)]
        [InlineData("", "secret")]
        [InlineData("secret", "")]
        [InlineData(null, null)]
        public void NullOrEmpty_Operand_NeverMatches(string? presented, string? expected)
        {
            TokenComparison.FixedTimeEquals(presented, expected).ShouldBeFalse();
        }
    }
}
