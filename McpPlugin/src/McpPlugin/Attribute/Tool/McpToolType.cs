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
    /// Specifies the type of an MCP tool.
    /// </summary>
    public enum McpToolType
    {
        /// <summary>
        /// Standard tool — exposed to MCP clients and AI agents via the MCP protocol.
        /// </summary>
        Standard = 0,

        /// <summary>
        /// System tool — available via the HTTP API (<c>/api/system-tools/</c>) but NOT
        /// exposed to MCP clients or AI agents. Used for internal operations like
        /// skill file generation.
        /// </summary>
        System = 1
    }
}
