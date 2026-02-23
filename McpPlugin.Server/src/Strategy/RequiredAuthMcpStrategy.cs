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
    public class RequiredAuthMcpStrategy : IMcpConnectionStrategy
    {
        // Set once at startup by ConfigureAuthentication; null means dynamic-pairing mode.
        private string? _serverToken;

        public Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.required;

        public bool AllowMultipleConnections => true;

        public void Validate(DataArguments dataArguments)
        {
            // auth=required mode: a server token at launch is optional.
            // When configured, only that single token is accepted from both plugins and MCP clients.
            // When absent, any token is accepted; plugins and clients are paired by matching token value.
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // Store for use in OnPluginConnected — SignalR hub has no [Authorize], so we enforce manually.
            _serverToken = dataArguments.Token;
            // ServerToken may be null — means "accept any token, pair by equality"
            options.ServerToken = dataArguments.Token;
            options.RequireToken = true;
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string, string?> disconnectClient)
        {
            if (string.IsNullOrEmpty(token))
            {
                // auth-required mode: plugins must provide a token; reject tokenless connections
                var reason = "Connection rejected: auth=required mode demands a token but none was provided.";
                logger.LogWarning("auth-required mode: plugin connected without a token, disconnecting {ConnectionId}.", connectionId);
                disconnectClient(connectionId, reason);
                return;
            }
            if (!string.IsNullOrEmpty(_serverToken) && !string.Equals(token, _serverToken, StringComparison.Ordinal))
            {
                // auth-required mode: server has an explicit token configured; plugin token must match
                var reason = "Connection rejected: the provided token does not match the server token.";
                logger.LogWarning("auth-required mode: plugin token does not match server token, disconnecting {ConnectionId}.", connectionId);
                disconnectClient(connectionId, reason);
                return;
            }
            // auth-required mode: register with token; allows multiple simultaneous connections
            ClientUtils.AddClient(hubType, connectionId, logger, token);
        }

        public void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
        {
            ClientUtils.RemoveClient(hubType, connectionId, logger);
        }

        public string? ResolveConnectionId(string? token, int retryOffset)
        {
            // auth-required mode: token must match a registered plugin; no fallback allowed
            return ClientUtils.GetConnectionIdByToken(token);
        }

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
        {
            // auth-required mode: only notify the MCP session paired with this plugin
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
            // auth-required mode: deny unscoped access — connection has no token
            return new McpClientData();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetServerData(token);
            // auth-required mode: deny unscoped access — connection has no token
            return new McpServerData();
        }
    }
}
