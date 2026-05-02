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
    public class RequiredAuthMcpStrategy : IMcpConnectionStrategy
    {
        // Set once at startup by ConfigureAuthentication; null means dynamic-pairing mode.
        // volatile ensures the write in ConfigureAuthentication is visible to all threads
        // that subsequently read it in OnPluginConnected (multiple SignalR threads).
        private volatile string? _serverToken;

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
            // Store the configured server token so OnPluginConnected can apply the same rules used by
            // TokenAuthenticationHandler (via streamableHttp's RequireAuthorization) and handle plugin/client pairing.
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
            // Try the per-session token first (set by HTTP transport per-request via McpSessionTokenContext).
            if (!string.IsNullOrEmpty(token))
                return ClientUtils.GetConnectionIdByToken(token);

            // Session token is null — stdio transport has no HTTP context and never modifies
            // McpSessionTokenContext.CurrentToken. Fall back to the server-configured token so
            // that the plugin registered with that token can still be reached.
            if (!string.IsNullOrEmpty(_serverToken))
                return ClientUtils.GetConnectionIdByToken(_serverToken);

            // Dynamic pairing mode (no server token configured) without a session token:
            // cannot determine the target plugin — return null to trigger retry.
            return null;
        }

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
        {
            // auth-required mode: only notify the MCP session paired with this plugin
            var pluginToken = ClientUtils.GetTokenByConnectionId(pluginConnectionId);
            if (pluginToken == null)
                return false;
            return string.Equals(pluginToken, sessionId, StringComparison.Ordinal);
        }

        public NotificationTarget ResolveNotificationTarget(string? routingToken)
        {
            // auth-required mode: a client-lifecycle notification only has a legitimate
            // recipient when the routing token maps to a registered plugin. Broadcasting
            // when the mapping is missing leaks foreign Tier 2 (DCR) and Tier 3 (ServerToken)
            // sessions into every connected plugin's active-client list (issue #102).
            if (!string.IsNullOrEmpty(routingToken))
            {
                var directMatch = ClientUtils.GetConnectionIdByToken(routingToken);
                if (directMatch != null)
                    return NotificationTarget.Specific(directMatch);
            }

            // Stdio transport never modifies McpSessionTokenContext.CurrentToken, so the
            // session-level routing token may be null. Mirror ResolveConnectionId by falling
            // back to the server-configured token so the plugin paired with that token still
            // receives notifications produced over stdio.
            if (!string.IsNullOrEmpty(_serverToken))
            {
                var serverMatch = ClientUtils.GetConnectionIdByToken(_serverToken);
                if (serverMatch != null)
                    return NotificationTarget.Specific(serverMatch);
            }

            // No addressable recipient: the notification belongs to a Tier 2 / Tier 3 session
            // (or a plugin that has not yet (re)connected). Dropping is correct — broadcasting
            // would pollute unrelated tenants.
            return NotificationTarget.Drop();
        }

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetClientDataByToken(token);
            // auth-required mode: deny unscoped access — connection has no token
            return new McpClientData();
        }

        public McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetAllClientData(token).ToArray();
            // auth-required mode: deny unscoped access — connection has no token
            return Array.Empty<McpClientData>();
        }

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
        {
            var token = ClientUtils.GetTokenByConnectionId(connectionId);
            if (token != null)
                return sessionTracker.GetServerDataByToken(token);
            // auth-required mode: deny unscoped access — connection has no token
            return new McpServerData();
        }
    }
}
