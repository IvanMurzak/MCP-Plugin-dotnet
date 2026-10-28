/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Whether a tool is listed in MCP <c>tools/list</c>. Visibility only shapes the catalogue an MCP client
    /// sees (its context size); it is NOT an access control: <c>tools/call</c> runs a hidden tool by name.
    /// </summary>
    public interface IVisible
    {
        bool Visible { set; get; }
    }
}
