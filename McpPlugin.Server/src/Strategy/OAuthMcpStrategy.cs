/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// OAuth resource-server connection strategy (mcp-authorize b2). Its responsibility in b2 is the
    /// AUTH configuration: it flags the <see cref="TokenAuthenticationHandler"/> to run the OAuth
    /// validation path (ES256/JWKS + introspection). The account+instance PAIRING plane (routing by
    /// <c>sub</c>) arrives in b3; until then the interim routing/notification behavior is delegated
    /// to the existing token-based <see cref="RequiredAuthMcpStrategy"/> so the server remains
    /// functional. No routing test depends on this interim delegation.
    /// </summary>
    public sealed class OAuthMcpStrategy : IMcpConnectionStrategy
    {
        // Interim routing engine (token-based). Replaced by the account+instance registry in b3.
        private readonly RequiredAuthMcpStrategy _routing = new RequiredAuthMcpStrategy();

        public Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.oauth;

        public bool AllowMultipleConnections => true;

        public void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrWhiteSpace(dataArguments.AuthIssuer))
                throw new ArgumentException("auth=oauth mode requires --auth-issuer (the authorization server URL).");
            if (string.IsNullOrWhiteSpace(dataArguments.PublicUrl))
                throw new ArgumentException("auth=oauth mode requires --public-url (this server's canonical resource id).");
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // OAuth mode: the handler validates the presented token against the AS (JWKS +
            // introspection). No pre-shared ServerToken; RequireToken must be true so the handler
            // runs on the (RequireAuthorization-gated) MCP endpoint.
            options.OAuthMode = true;
            options.ServerToken = null;
            options.RequireToken = true;
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token, ILogger logger, Action<string, string?> disconnectClient)
            => _routing.OnPluginConnected(hubType, connectionId, token, logger, disconnectClient);

        public void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger)
            => _routing.OnPluginDisconnected(hubType, connectionId, logger);

        public string? ResolveConnectionId(string? token, int retryOffset)
            => _routing.ResolveConnectionId(token, retryOffset);

        public bool ShouldNotifySession(string pluginConnectionId, string sessionId)
            => _routing.ShouldNotifySession(pluginConnectionId, sessionId);

        public NotificationTarget ResolveNotificationTarget(string? routingToken)
            => _routing.ResolveNotificationTarget(routingToken);

        public McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker)
            => _routing.GetClientData(connectionId, sessionTracker);

        public McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker)
            => _routing.GetAllClientData(connectionId, sessionTracker);

        public McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker)
            => _routing.GetServerData(connectionId, sessionTracker);
    }
}
