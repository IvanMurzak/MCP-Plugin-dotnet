/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using com.IvanMurzak.McpPlugin.Common.Model;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A tool's own error result answers the direct REST channel with 400, but on the MCP channel it
    /// must stay an ordinary tool result with <c>isError: true</c> — the error kind only picks an HTTP status.
    /// </summary>
    public class ToolOwnErrorMcpChannelTests
    {
        [Fact]
        public void ToCallToolResult_ToolOwnError_IsErrorResultWithMessage()
        {
            var response = ResponseCallTool.Error("No Renderers found");

            var result = response.ToCallToolResult();

            result.IsError.ShouldBe(true);
            result.Content.Count.ShouldBe(1);
            result.Content.Single().ShouldBeOfType<TextContentBlock>();
            ((TextContentBlock)result.Content.Single()).Text.ShouldBe("No Renderers found");
        }

        [Fact]
        public void ToCallToolResult_ErrorOfEveryKind_IsErrorResult()
        {
            foreach (var kind in System.Enum.GetValues<ResponseErrorKind>())
            {
                var result = ResponseCallTool.Error("declined", kind).ToCallToolResult();

                result.IsError.ShouldBe(true, $"kind {kind}");
            }
        }
    }
}
