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
    public interface IMcpConnectionStrategy
    {
        Consts.MCP.Server.AuthOption AuthOption { get; }

        /// <summary>
        /// Whether this auth option allows multiple simultaneous plugin connections.
        /// </summary>
        bool AllowMultipleConnections { get; }

        /// <summary>
        /// Validates the DataArguments configuration for this auth option at startup.
        /// Throws if the configuration is structurally invalid (e.g. oauth requires an issuer +
        /// public URL).
        /// </summary>
        void Validate(DataArguments dataArguments);

        /// <summary>
        /// Configures authentication options based on the auth option.
        /// For AuthOption.oauth: sets OAuthMode=true so the handler validates ES256 JWT / opaque-PAT
        /// introspection. For AuthOption.none: anonymous — no token gate.
        /// </summary>
        void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments);

        /// <summary>
        /// Called when a plugin connects via SignalR. Handles registration and, for
        /// AuthOption.none, disconnects any other client to enforce the single-connection
        /// invariant. The disconnectClient callback accepts a connection ID and an
        /// optional human-readable reason that is forwarded to the plugin for logging.
        /// </summary>
        void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string, string?> disconnectClient);

        /// <summary>
        /// Called when a plugin disconnects from SignalR.
        /// </summary>
        void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger);

        /// <summary>
        /// Resolves the SignalR connection ID to route a request to,
        /// given the current MCP session token and a retry offset.
        /// Returns null if no connection is available or (for AuthOption.oauth) if the session's
        /// account resolves to no live instance — routing is strictly account-scoped (fail closed).
        /// </summary>
        string? ResolveConnectionId(string? token, int retryOffset);

        /// <summary>
        /// Determines whether a notification from a given plugin connection
        /// should be forwarded to this MCP session (identified by sessionId).
        /// </summary>
        bool ShouldNotifySession(string pluginConnectionId, string sessionId);

        /// <summary>
        /// Resolves the destination of a client-lifecycle notification (connect/disconnect)
        /// that originated from an MCP session carrying <paramref name="routingToken"/>.
        /// For AuthOption.oauth: returns <see cref="NotificationTarget.Specific"/> for the session's
        /// account-resolved instance, otherwise <see cref="NotificationTarget.Drop"/> — broadcasting
        /// would leak foreign clients into another account's active-client list.
        /// For AuthOption.none: returns <see cref="NotificationTarget.Broadcast"/> because the
        /// strategy enforces a single-plugin invariant, so the broadcast has at most one recipient.
        /// </summary>
        NotificationTarget ResolveNotificationTarget(string? routingToken);

        /// <summary>
        /// Retrieves McpClientData scoped to the connection's account.
        /// For AuthOption.oauth: returns an empty <see cref="McpClientData"/> when the connection
        /// resolves to no account — unscoped access is denied (fail closed).
        /// </summary>
        McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker);

        /// <summary>
        /// Retrieves all active McpClientData entries scoped to the connection's account.
        /// For AuthOption.oauth: returns an empty array when the connection resolves to no account.
        /// For AuthOption.none: returns all active sessions regardless of token.
        /// </summary>
        McpClientData[] GetAllClientData(string? connectionId, IMcpSessionTracker sessionTracker);

        /// <summary>
        /// Retrieves McpServerData scoped to the connection's account.
        /// For AuthOption.oauth: returns an empty <see cref="McpServerData"/> when the connection
        /// resolves to no account — unscoped access is denied (fail closed).
        /// </summary>
        McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker);
    }
}
