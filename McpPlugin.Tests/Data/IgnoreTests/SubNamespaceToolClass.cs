/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Tests.Data.Ignored.SubNamespace
{
    [AiToolType]
    internal class SubNamespaceToolClass
    {
        [AiTool("sub-namespace-tool", "Tool in sub-namespace")]
        public static string TestTool() => "test";
    }

    [AiPromptType]
    internal class SubNamespacePromptClass
    {
        [AiPrompt(Name = "sub-namespace-prompt")]
        public static string TestPrompt() => "test prompt";
    }
}
