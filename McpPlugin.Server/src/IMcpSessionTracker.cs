/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Server
{
    public interface IMcpSessionTracker
    {
        McpClientData GetClientData();
        McpClientData GetClientData(string sessionId);
        McpServerData GetServerData();
        McpServerData GetServerData(string sessionId);
        IReadOnlyList<McpClientData> GetAllClientData();
        void Update(string sessionId, McpClientData clientData, McpServerData serverData);
        void Remove(string sessionId);
    }
}
