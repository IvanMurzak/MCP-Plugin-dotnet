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
    [McpPluginPromptType]
    public static class AnnotatedPromptClass
    {
        [McpPluginPrompt(Name = "prompt-enabled-default")]
        public static string EnabledDefault() => "default";

        [McpPluginPrompt(Name = "prompt-enabled-true", Enabled = true)]
        public static string EnabledTrue() => "enabled";

        [McpPluginPrompt(Name = "prompt-enabled-false", Enabled = false)]
        public static string EnabledFalse() => "disabled";
    }
}
