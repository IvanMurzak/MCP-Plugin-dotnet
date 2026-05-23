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
    public static class MixedToolTypeClass
    {
        [AiTool("standard-tool-a", "Standard Tool A")]
        public static string StandardA() => "a";

        [AiTool("standard-tool-b", "Standard Tool B")]
        public static string StandardB() => "b";

        [AiTool("system-tool-x", "System Tool X", ToolType = McpToolType.System)]
        public static string SystemX() => "x";

        [AiTool("system-tool-y", "System Tool Y", ToolType = McpToolType.System)]
        public static string SystemY() => "y";

        [AiTool("standard-default", "Default ToolType")]
        public static string DefaultType() => "default";
    }
}
