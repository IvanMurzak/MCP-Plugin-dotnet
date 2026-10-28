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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    /// <summary>
    /// The offline <c>token</c> auth plane (mcp-authorize g6) — the loopback single-project counterpart
    /// of the account-scoped <see cref="AccountMcpStrategy"/>. Routes like <see cref="NoAuthMcpStrategy"/>
    /// (one editor and one runtime connection, broadcast notifications; see
    /// <see cref="SingleProjectMcpStrategy"/>) but gated on a single static shared secret:
    /// <list type="bullet">
    ///   <item><b>Auth config</b> — flags the local-token validation path on
    ///   <see cref="TokenAuthenticationOptions"/> so the streamableHttp MCP endpoint is bearer-gated
    ///   (constant-time compare), never anonymous.</item>
    ///   <item><b>Hub gate</b> — <c>OnPluginConnected</c> rejects a tokenless or mismatched plugin
    ///   connection (constant-time compare) before it is registered.</item>
    /// </list>
    /// A loopback single-project server needs one plugin plus one secret; the multi-tenant token-equality
    /// pairing of the deleted b5 <c>RequiredAuthMcpStrategy</c> is intentionally NOT reintroduced.
    /// </summary>
    public sealed class LocalTokenMcpStrategy : SingleProjectMcpStrategy
    {
        // Set once at startup by Validate / ConfigureAuthentication (both run before any connection).
        // volatile so the write is visible to the SignalR threads that read it in OnPluginConnected.
        private volatile string? _serverToken;

        public override Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.token;

        public override void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrWhiteSpace(dataArguments.Token))
                throw new ArgumentException(
                    "auth=token mode requires a non-empty --token (or MCP_PLUGIN_TOKEN) shared secret.");

            base.Validate(dataArguments);
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

        public override void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // Offline token mode: the handler validates the presented bearer against this static secret
            // with a constant-time compare. Not the OAuth (JWKS/introspection) path.
            options.OAuthMode = false;
            options.LocalTokenMode = true;
            options.LocalToken = dataArguments.Token;
            _serverToken = dataArguments.Token;
        }

        public override void OnPluginConnected(Type hubType, string connectionId, string? token, ClientRole? declaredRole,
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

            base.OnPluginConnected(hubType, connectionId, token, declaredRole, logger, disconnectClient);
        }
    }
}
