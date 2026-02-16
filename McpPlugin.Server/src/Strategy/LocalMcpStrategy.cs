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
    public class LocalMcpStrategy : IMcpConnectionStrategy
    {
        public Consts.MCP.Server.DeploymentMode DeploymentMode
            => Consts.MCP.Server.DeploymentMode.local;

        public bool AllowMultipleConnections => false;

        public void Validate(DataArguments dataArguments)
        {
            // LOCAL mode: token is optional, no strict validation needed
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            options.ServerToken = dataArguments.Token;
            options.RequireToken = !string.IsNullOrEmpty(dataArguments.Token);
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string> disconnectClient)
        {
            ClientUtils.AddClient(hubType, connectionId, logger, token);

            // LOCAL mode: enforce single connection by disconnecting all others
            foreach (var otherId in ClientUtils.GetAllConnectionIds(hubType).Where(id => id != connectionId).ToList())
            {
                logger.LogInformation("LOCAL mode: disconnecting other client '{0}' for {1}.", otherId, hubType.Name);
                disconnectClient(otherId);
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
            // LOCAL mode: broadcast all notifications to all sessions
            return true;
        }

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            return sessionTracker.GetClientData();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            return sessionTracker.GetServerData();
        }
    }
}
