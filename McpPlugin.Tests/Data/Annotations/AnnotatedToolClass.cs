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
    [AiToolType]
    public static class AnnotatedToolClass
    {
        [AiTool("tool-no-hints", "Tool With No Hints")]
        public static void NoHints() { }

        [AiTool("tool-readonly", "Read-Only Tool", ReadOnlyHint = true)]
        public static void ReadOnly() { }

        [AiTool("tool-destructive-false", "Non-Destructive Tool", DestructiveHint = false)]
        public static void DestructiveFalse() { }

        [AiTool("tool-idempotent", "Idempotent Tool", IdempotentHint = true)]
        public static void Idempotent() { }

        [AiTool("tool-open-world", "Open World Tool", OpenWorldHint = true)]
        public static void OpenWorld() { }

        [AiTool("tool-all-hints", "All Hints Tool",
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
            OpenWorldHint = false)]
        public static void AllHints() { }

        [AiTool("tool-enabled-default", "Tool With Default Enabled")]
        public static void EnabledDefault() { }

        [AiTool("tool-enabled-true", "Tool With Enabled True", Enabled = true)]
        public static void EnabledTrue() { }

        [AiTool("tool-enabled-false", "Tool With Enabled False", Enabled = false)]
        public static void EnabledFalse() { }
    }
}
