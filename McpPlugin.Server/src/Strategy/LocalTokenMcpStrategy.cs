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
using System.Linq;
using System.Net;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The offline <c>token</c> auth plane (mcp-authorize g6) — the loopback single-project counterpart
    /// of the account-scoped <see cref="AccountMcpStrategy"/>. Modeled on <see cref="NoAuthMcpStrategy"/>
    /// (single plugin connection, broadcast routing) but gated on a single static shared secret:
    /// <list type="bullet">
    ///   <item><b>Auth config</b> — flags the local-token validation path on
    ///   <see cref="TokenAuthenticationOptions"/> so the streamableHttp MCP endpoint is bearer-gated
    ///   (constant-time compare), never anonymous.</item>
    ///   <item><b>Hub gate</b> — <see cref="OnPluginConnected"/> rejects a tokenless or mismatched plugin
    ///   connection (constant-time compare), then enforces the single-connection invariant.</item>
    ///   <item><b>Routing / notifications / data</b> — single connection, so routing resolves the one
    ///   registered plugin and notifications broadcast (exactly like <see cref="NoAuthMcpStrategy"/>).</item>
    /// </list>
    /// A loopback single-project server needs one plugin plus one secret; the multi-tenant token-equality
    /// pairing of the deleted b5 <c>RequiredAuthMcpStrategy</c> is intentionally NOT reintroduced.
    /// </summary>
    public sealed class LocalTokenMcpStrategy : IMcpConnectionStrategy
    {
        // Set once at startup by Validate / ConfigureAuthentication (both run before any connection).
        // volatile so the write is visible to the SignalR threads that read it in OnPluginConnected.
        private volatile string? _serverToken;

        public Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.token;

        // Single-project loopback server: exactly one plugin connection, like no-auth mode.
        public bool AllowMultipleConnections => false;

        public void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrWhiteSpace(dataArguments.Token))
                throw new ArgumentException(
                    "auth=token mode requires a non-empty --token (or MCP_PLUGIN_TOKEN) shared secret.");

            _serverToken = dataArguments.Token;

            // Owner ruling (design g5 §Security, sanity-checked): a token over cleartext local HTTP is
            // LAN-sniffable, so binding non-loopback is WARNED but ALLOWED (enables LAN use) rather than
            // rejected. Reuse the tested bind→address resolution so the warning matches the real listener.
            if (IsNonLoopbackBind(dataArguments.Bind))
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(
                    "auth=token mode is bound to a non-loopback address (--bind '{0}'). The shared token travels over cleartext local HTTP and is sniffable on the LAN. Prefer loopback-only, or terminate TLS in front of the server.",
                    dataArguments.Bind);
            }
        }

        static bool IsNonLoopbackBind(string? bind)
        {
            try
            {
                return ExtensionsWebHost.ResolveBindAddresses(bind).Any(addr => !IPAddress.IsLoopback(addr));
            }
            catch (ArgumentException)
            {
                // An unparseable --bind is surfaced by the listener setup; do not warn from here.
                return false;
            }
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // Offline token mode: the handler validates the presented bearer against this static secret
            // with a constant-time compare. Not the OAuth (JWKS/introspection) path.
            options.OAuthMode = false;
            options.LocalTokenMode = true;
            options.LocalToken = dataArguments.Token;
            _serverToken = dataArguments.Token;
        }

        public void OnPluginConnected(Type hubType, string connectionId, string? token,
            ILogger logger, Action<string, string?> disconnectClient)
        {
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning("auth=token mode: plugin connected without a token, disconnecting {ConnectionId}.", connectionId);
                disconnectClient(connectionId, "Connection rejected: auth=token mode requires a token but none was provided.");
                return;
            }

            if (!TokenComparison.FixedTimeEquals(token, _serverToken))
            {
                logger.LogWarning("auth=token mode: plugin token does not match the server token, disconnecting {ConnectionId}.", connectionId);
                disconnectClient(connectionId, "Connection rejected: the provided token does not match the server token.");
                return;
            }

            ClientUtils.AddClient(hubType, connectionId, logger, token);

            // Single-connection invariant: disconnect any previously-registered plugin (like no-auth mode).
            foreach (var otherId in ClientUtils.GetAllConnectionIds(hubType).Where(id => id != connectionId).ToList())
            {
                logger.LogInformation("auth=token mode: disconnecting other client '{0}' for {1}.", otherId, hubType.Name);
                ClientUtils.RemoveClient(hubType, otherId, logger);
                disconnectClient(otherId, "A newer plugin connection has replaced this one (auth=token mode allows only one connection).");
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
            // Single-connection mode: broadcast all notifications to the one session.
            return true;
        }

        public NotificationTarget ResolveNotificationTarget(string? routingToken)
        {
            // OnPluginConnected enforces a single plugin connection, so a broadcast targets at most one
            // recipient. Routing token is ignored (there is no per-token multi-tenant mapping here).
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
