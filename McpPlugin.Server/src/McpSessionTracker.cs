/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpSessionTracker : IMcpSessionTracker
    {
        readonly object _lock = new();
        McpClientData? _clientData;
        McpServerData? _serverData;

        public McpClientData GetClientData()
        {
            lock (_lock)
            {
                return _clientData ?? new McpClientData { IsConnected = false };
            }
        }

        public McpServerData GetServerData()
        {
            lock (_lock)
            {
                return _serverData ?? new McpServerData { IsAiAgentConnected = false };
            }
        }

        public void Update(McpClientData clientData, McpServerData serverData)
        {
            lock (_lock)
            {
                _clientData = clientData;
                _serverData = serverData;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _clientData = null;
                _serverData = null;
            }
        }
    }
}
