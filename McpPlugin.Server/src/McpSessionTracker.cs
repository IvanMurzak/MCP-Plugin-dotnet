/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpSessionTracker : IMcpSessionTracker
    {
        readonly object _lock = new();
        readonly IDataArguments _dataArguments;
        readonly Common.Version _version;
        McpClientData? _clientData;
        McpServerData? _serverData;

        public McpSessionTracker(IDataArguments dataArguments, Common.Version version)
        {
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _version = version ?? throw new ArgumentNullException(nameof(version));
        }

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
                return _serverData ?? new McpServerData
                {
                    IsAiAgentConnected = false,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    ServerApiVersion = _version.Api,
                    ServerTransport = _dataArguments.ClientTransport
                };
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
