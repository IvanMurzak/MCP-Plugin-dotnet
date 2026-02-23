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
        /// Throws if the configuration is structurally invalid.
        /// Note: for AuthOption.required, a launch-time token is optional — absence is not an error.
        /// </summary>
        void Validate(DataArguments dataArguments);

        /// <summary>
        /// Configures authentication options based on the auth option.
        /// For AuthOption.required: always sets RequireToken=true; ServerToken may be null
        /// (dynamic mode — any token accepted, pairing enforced by token equality).
        /// </summary>
        void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments);

        /// <summary>
        /// Called when a plugin connects via SignalR. Handles registration and
        /// optionally disconnects other clients (AuthOption.none) or connections without a token
        /// (AuthOption.required).
        /// </summary>
        void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string> disconnectClient);

        /// <summary>
        /// Called when a plugin disconnects from SignalR.
        /// </summary>
        void OnPluginDisconnected(Type hubType, string connectionId, ILogger logger);

        /// <summary>
        /// Resolves the SignalR connection ID to route a request to,
        /// given the current MCP session token and a retry offset.
        /// Returns null if no connection is available or (for AuthOption.required)
        /// if the token does not match any registered plugin — no fallback is allowed.
        /// </summary>
        string? ResolveConnectionId(string? token, int retryOffset);

        /// <summary>
        /// Determines whether a notification from a given plugin connection
        /// should be forwarded to this MCP session (identified by sessionId).
        /// </summary>
        bool ShouldNotifySession(string pluginConnectionId, string sessionId);

        /// <summary>
        /// Retrieves McpClientData scoped to the connection's token.
        /// For AuthOption.required: returns an empty <see cref="McpClientData"/> if the
        /// connection carries no token — unscoped access is denied.
        /// </summary>
        McpClientData GetClientData(string? connectionId, IMcpSessionTracker sessionTracker);

        /// <summary>
        /// Retrieves McpServerData scoped to the connection's token.
        /// For AuthOption.required: returns an empty <see cref="McpServerData"/> if the
        /// connection carries no token — unscoped access is denied.
        /// </summary>
        McpServerData GetServerData(string? connectionId, IMcpSessionTracker sessionTracker);
    }
}
