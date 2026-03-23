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

        // Keyed by physicalId (unique per MCP connection).
        // RoutingToken is the Bearer token used for plugin notification routing; null in no-auth mode.
        readonly ConcurrentDictionary<string, (string? RoutingToken, McpClientData ClientData, McpServerData ServerData)> _sessions = new();

        // Reference counting: tracks how many active McpServerService instances share a physicalId.
        // Guarded by _refCountLock for atomic decrement-and-check in Remove().
        readonly object _refCountLock = new object();
        readonly Dictionary<string, int> _refCounts = new();

        public McpSessionTracker(ILogger<McpSessionTracker> logger, IDataArguments dataArguments, Common.Version version)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _version = version ?? throw new ArgumentNullException(nameof(version));
        }

        public int ActiveSessionCount => _sessions.Count;

        public McpClientData GetClientData()
        {
            var entry = _sessions.Values.FirstOrDefault(x => x.ClientData.IsConnected);
            return entry.ClientData ?? new McpClientData { IsConnected = false };
        }

        public McpClientData GetClientData(string physicalId)
        {
            if (_sessions.TryGetValue(physicalId, out var entry))
                return entry.ClientData;
            return new McpClientData { IsConnected = false };
        }

        public McpClientData GetClientDataByToken(string? routingToken)
        {
            if (string.IsNullOrEmpty(routingToken))
                return GetClientData();

            McpClientData? fallback = null;
            foreach (var entry in _sessions.Values)
            {
                if (!string.Equals(entry.RoutingToken, routingToken, StringComparison.Ordinal))
                    continue;
                if (entry.ClientData.IsConnected)
                    return entry.ClientData;
                fallback ??= entry.ClientData;
            }
            return fallback ?? new McpClientData { IsConnected = false };
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

        public McpServerData GetServerData(string physicalId)
        {
            if (_sessions.TryGetValue(physicalId, out var entry))
                return entry.ServerData;
            return new McpServerData
            {
                IsAiAgentConnected = false,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ServerApiVersion = _version.Api,
                ServerTransport = _dataArguments.ClientTransport
            };
        }

        public McpServerData GetServerDataByToken(string? routingToken)
        {
            if (string.IsNullOrEmpty(routingToken))
                return GetServerData();

            McpServerData? fallback = null;
            foreach (var entry in _sessions.Values)
            {
                if (!string.Equals(entry.RoutingToken, routingToken, StringComparison.Ordinal))
                    continue;
                if (entry.ServerData.IsAiAgentConnected)
                    return entry.ServerData;
                fallback ??= entry.ServerData;
            }
            return fallback ?? new McpServerData
            {
                IsAiAgentConnected = false,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ServerApiVersion = _version.Api,
                ServerTransport = _dataArguments.ClientTransport
            };
        }

        public IReadOnlyList<McpClientData> GetAllClientData(string? routingToken = null)
        {
            if (routingToken == null)
                return _sessions.Values.Select(x => x.ClientData).ToList();

            return _sessions.Values
                .Where(x => string.Equals(x.RoutingToken, routingToken, StringComparison.Ordinal))
                .Select(x => x.ClientData)
                .ToList();
        }

        public void Update(string physicalId, string? routingToken, McpClientData clientData, McpServerData serverData)
        {
            var value = (routingToken, clientData, serverData);
            var isNew = true;
            _sessions.AddOrUpdate(physicalId, value, (_, _) =>
            {
                isNew = false;
                return value;
            });
            _logger.LogDebug("Session {action}. PhysicalId: {physicalId}, RoutingToken: {hasToken}, IsConnected: {isConnected}, ClientName: {clientName}, TotalSessions: {total}.",
                isNew ? "added" : "updated", physicalId, routingToken != null ? "present" : "absent", clientData.IsConnected, clientData.ClientName, _sessions.Count);
        }

        public void AddRef(string physicalId)
        {
            int newCount;
            lock (_refCountLock)
            {
                _refCounts.TryGetValue(physicalId, out var count);
                newCount = count + 1;
                _refCounts[physicalId] = newCount;
            }
            _logger.LogDebug("Session ref incremented. PhysicalId: {physicalId}, Refs: {refs}, TotalSessions: {total}.",
                physicalId, newCount, _sessions.Count);
        }

        public bool Remove(string physicalId)
        {
            bool isLast;
            bool sessionRemoved = false;
            lock (_refCountLock)
            {
                if (_refCounts.TryGetValue(physicalId, out var count) && count > 1)
                {
                    _refCounts[physicalId] = count - 1;
                    isLast = false;
                }
                else
                {
                    _refCounts.Remove(physicalId);
                    sessionRemoved = _sessions.TryRemove(physicalId, out _);
                    isLast = true;
                }
            }

            if (!isLast)
            {
                _logger.LogDebug("Session ref decremented, still active. PhysicalId: {physicalId}, TotalSessions: {total}.",
                    physicalId, _sessions.Count);
                return false;
            }

            if (sessionRemoved)
                _logger.LogDebug("Session removed. PhysicalId: {physicalId}, TotalSessions: {total}.", physicalId, _sessions.Count);
            else
                _logger.LogDebug("Session not found for removal. PhysicalId: {physicalId}, TotalSessions: {total}.", physicalId, _sessions.Count);

            return true;
        }
    }
}
