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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpSessionTracker : IMcpSessionTracker
    {
        readonly IDataArguments _dataArguments;
        readonly Common.Version _version;
        readonly ConcurrentDictionary<string, (McpClientData ClientData, McpServerData ServerData)> _sessions = new();

        public McpSessionTracker(IDataArguments dataArguments, Common.Version version)
        {
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public McpClientData GetClientData()
        {
            var entry = _sessions.Values.FirstOrDefault(x => x.ClientData.IsConnected);
            return entry.ClientData ?? new McpClientData { IsConnected = false };
        }

        public McpServerData GetServerData()
        {
            var hasAnyConnected = _sessions.Values.Any(x => x.ServerData.IsAiAgentConnected);
            return new McpServerData
            {
                IsAiAgentConnected = hasAnyConnected,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ServerApiVersion = _version.Api,
                ServerTransport = _dataArguments.ClientTransport
            };
        }

        public IReadOnlyList<McpClientData> GetAllClientData()
        {
            return _sessions.Values.Select(x => x.ClientData).ToList();
        }

        public void Update(string sessionId, McpClientData clientData, McpServerData serverData)
        {
            _sessions[sessionId] = (clientData, serverData);
        }

        public void Remove(string sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }
}
