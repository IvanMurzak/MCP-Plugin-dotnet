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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    public class RemoteMcpStrategy : IMcpConnectionStrategy
    {
        public Consts.MCP.Server.AuthOption DeploymentMode
            => Consts.MCP.Server.AuthOption.required;

        public bool AllowMultipleConnections => true;

        public void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrEmpty(dataArguments.Token))
            {
                throw new InvalidOperationException(
                    "REMOTE deployment mode requires a token. " +
                    "Set via --token=<value> or MCP_PLUGIN_TOKEN environment variable.");
            }
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            options.ServerToken = dataArguments.Token;
            options.RequireToken = true;
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string> disconnectClient)
        {
            // REMOTE mode: allow multiple connections, no disconnection
            ClientUtils.AddClient(hubType, connectionId, logger, token);
        }

        public void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
        {
            ClientUtils.RemoveClient(hubType, connectionId, logger);
        }

        public string? ResolveConnectionId(string? token, int retryOffset)
        {
            // REMOTE mode: prefer token-based routing, fallback to round-robin as safety net
            return ClientUtils.GetConnectionIdByToken(token)
                ?? ClientUtils.GetBestConnectionId(typeof(McpServerHub), retryOffset);
        }

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
        {
            // REMOTE mode: only notify the MCP session paired with this plugin
            var pluginToken = ClientUtils.GetTokenByConnectionId(pluginConnectionId);
            if (pluginToken == null)
                return false;
            return string.Equals(pluginToken, sessionId, StringComparison.Ordinal);
        }

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetClientData(token);
            return sessionTracker.GetClientData();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetServerData(token);
            return sessionTracker.GetServerData();
        }
    }
}
