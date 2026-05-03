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
using System.Linq;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    public class NoAuthMcpStrategy : IMcpConnectionStrategy
    {
        public Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.none;

        public bool AllowMultipleConnections => false;

        public void Validate(DataArguments dataArguments)
        {
            // no-auth mode: token is optional, no strict validation needed
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            options.ServerToken = null;
            options.RequireToken = false;
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string, string?> disconnectClient)
        {
            ClientUtils.AddClient(hubType, connectionId, logger, token);

            // no-auth mode: enforce single connection by disconnecting all others
            foreach (var otherId in ClientUtils.GetAllConnectionIds(hubType).Where(id => id != connectionId).ToList())
            {
                logger.LogInformation("no-auth mode: disconnecting other client '{0}' for {1}.", otherId, hubType.Name);
                // Remove immediately so routing cannot resolve this ID before OnPluginDisconnected fires.
                ClientUtils.RemoveClient(hubType, otherId, logger);
                disconnectClient(otherId, "A newer plugin connection has replaced this one (no-auth mode allows only one connection).");
            }
        }

        public void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
        {
            ClientUtils.RemoveClient(hubType, connectionId, logger);
        }

        public string? ResolveConnectionId(string? token, int retryOffset)
        {
            return ClientUtils.GetConnectionIdByToken(token)
                ?? ClientUtils.GetBestConnectionId(typeof(McpServerHub), retryOffset);
        }

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
        {
            // no-auth mode: broadcast all notifications to all sessions
            return true;
        }

        public NotificationTarget ResolveNotificationTarget(string? routingToken)
        {
            // no-auth mode: OnPluginConnected enforces a single plugin connection, so a
            // broadcast targets at most one recipient. Routing token is ignored (always null).
            return NotificationTarget.Broadcast();
        }

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            return sessionTracker.GetClientData();
        }

        public McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            return sessionTracker.GetAllClientData().ToArray();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            return sessionTracker.GetServerData();
        }
    }
}
