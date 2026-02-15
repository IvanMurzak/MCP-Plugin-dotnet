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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpSessionTracker : IMcpSessionTracker
    {
        readonly ILogger<McpSessionTracker> _logger;
        readonly IDataArguments _dataArguments;
        readonly Common.Version _version;
        readonly ConcurrentDictionary<string, (McpClientData ClientData, McpServerData ServerData)> _sessions = new();

        public McpSessionTracker(ILogger<McpSessionTracker> logger, IDataArguments dataArguments, Common.Version version)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public McpClientData GetClientData()
        {
            var entry = _sessions.Values.FirstOrDefault(x => x.ClientData.IsConnected);
            return entry.ClientData ?? new McpClientData { IsConnected = false };
        }

        public McpClientData GetClientData(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var entry))
                return entry.ClientData;

            return new McpClientData { IsConnected = false };
        }

        public McpServerData GetServerData()
        {
            var entry = _sessions.Values.FirstOrDefault(x => x.ServerData.IsAiAgentConnected);
            return entry.ServerData ?? new McpServerData
            {
                IsAiAgentConnected = false,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ServerApiVersion = _version.Api,
                ServerTransport = _dataArguments.ClientTransport
            };
        }

        public McpServerData GetServerData(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var entry))
                return entry.ServerData;

            return new McpServerData
            {
                IsAiAgentConnected = false,
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
            var value = (clientData, serverData);
            var isNew = true;
            _sessions.AddOrUpdate(sessionId, value, (_, _) =>
            {
                isNew = false;
                return value;
            });
            _logger.LogDebug("Session {action}. Key: {sessionId}, IsConnected: {isConnected}, ClientName: {clientName}, TotalSessions: {total}.",
                isNew ? "added" : "updated", sessionId, clientData.IsConnected, clientData.ClientName, _sessions.Count);
        }

        public void Remove(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out _))
                _logger.LogDebug("Session removed. Key: {sessionId}, TotalSessions: {total}.", sessionId, _sessions.Count);
            else
                _logger.LogDebug("Session not found for removal. Key: {sessionId}, TotalSessions: {total}.", sessionId, _sessions.Count);
        }
    }
}
