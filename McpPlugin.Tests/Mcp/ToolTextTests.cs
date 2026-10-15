/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    public class ToolTextTests
    {
        [Theory]
        [InlineData(ToolTextEscaping.Minimal, "a+b<c \"q\" \\ \n é", "\"a+b<c \\\"q\\\" \\\\ \\n é\"")]
        [InlineData(ToolTextEscaping.Minimal, "plain text", "\"plain text\"")]
        [InlineData(ToolTextEscaping.Ascii, "a+b é", "\"a+b \\u00e9\"")]
        [InlineData(ToolTextEscaping.Json, "a+b", "\"a\\u002Bb\"")]
        public void QuoteEscapesPerMode(ToolTextEscaping escaping, string text, string expected)
            => ToolText.Quote(text, escaping).ShouldBe(expected);

        [Fact]
        public void LoneSurrogatesAreEscapedAndPairsKept()
        {
            ToolText.Quote("x\ud800y", ToolTextEscaping.Minimal).ShouldBe("\"x\\ud800y\"");
            ToolText.Quote("😀", ToolTextEscaping.Minimal).ShouldBe("\"😀\"");
            ToolText.Quote("😀", ToolTextEscaping.Ascii).ShouldBe("\"\\ud83d\\ude00\"");
        }
    }
}
