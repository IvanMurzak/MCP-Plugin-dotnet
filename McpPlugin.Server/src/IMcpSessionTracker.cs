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

        /// <summary>
        /// Increments the reference count for the session. Call once per logical connection
        /// (e.g. from McpServerService.StartAsync) to track how many active connections
        /// share this sessionId.
        /// </summary>
        void AddRef(string sessionId);

        /// <summary>
        /// Decrements the reference count for the session. Removes the session entry only
        /// when the count reaches zero (i.e. the last connection with this sessionId ended).
        /// Returns <c>true</c> if this was the last reference and the session was removed,
        /// <c>false</c> if other connections with the same sessionId are still active.
        /// </summary>
        bool Remove(string sessionId);
    }
}
