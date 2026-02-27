/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin.Tests.Data.Annotations
{
    [McpPluginToolType]
    public static class AnnotatedToolClass
    {
        [McpPluginTool("tool-no-hints", "Tool With No Hints")]
        public static void NoHints() { }

        [McpPluginTool("tool-readonly", "Read-Only Tool", ReadOnlyHint = true)]
        public static void ReadOnly() { }

        [McpPluginTool("tool-destructive-false", "Non-Destructive Tool", DestructiveHint = false)]
        public static void DestructiveFalse() { }

        [McpPluginTool("tool-idempotent", "Idempotent Tool", IdempotentHint = true)]
        public static void Idempotent() { }

        [McpPluginTool("tool-open-world", "Open World Tool", OpenWorldHint = true)]
        public static void OpenWorld() { }

        [McpPluginTool("tool-all-hints", "All Hints Tool",
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
            OpenWorldHint = false)]
        public static void AllHints() { }
    }
}
