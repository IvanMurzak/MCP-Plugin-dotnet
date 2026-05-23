/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Tests.Data.Included
{
    [AiToolType]
    internal class IncludeTestToolClass
    {
        [AiTool("include-test-tool", "Test tool that should be included")]
        public static string TestTool() => "test";
    }

    [AiPromptType]
    internal class IncludeTestPromptClass
    {
        [AiPrompt(Name = "include-test-prompt")]
        public static string TestPrompt() => "test prompt";
    }
}
