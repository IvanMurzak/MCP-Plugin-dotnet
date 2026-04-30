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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class BaseHub<T> : Hub<T>, IDisposable
        where T : class, IClientDisconnectable
    {
        protected readonly ILogger _logger;
        protected readonly IMcpConnectionStrategy _strategy;
        protected readonly IAuthorizationWebhookService _authorizationWebhookService;
        protected readonly CompositeDisposable _disposables = new();
        protected readonly string _guid = Guid.NewGuid().ToString();
        /// <summary>
        /// Set to true if the connection is rejected by the authorization webhook.
        /// This flag prevents subsequent connection setup from executing after Context.Abort() is called.
        /// Marked volatile to ensure visibility across threads.
        /// </summary>
        protected volatile bool _connectionRejected = false;

        protected BaseHub(ILogger logger, IMcpConnectionStrategy strategy, IAuthorizationWebhookService authorizationWebhookService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _authorizationWebhookService = authorizationWebhookService ?? throw new ArgumentNullException(nameof(authorizationWebhookService));
            _logger.LogTrace("Ctor. {guid}", _guid);
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var token = httpContext?.Request.Query["access_token"].FirstOrDefault();
            if (string.IsNullOrEmpty(token))
            {
                var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
                if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = authHeader.Substring("Bearer ".Length).Trim();
            }

            // Short-circuit: in auth=required mode, an empty/missing token is always rejected
            // by RequiredAuthMcpStrategy.OnPluginConnected anyway. Skip the AuthorizationWebhook
            // round-trip — it would only return a "Missing bearerToken" denial and emit a Warning
            // log line on the webhook side. Cuts a documented production-log noise source for
            // tokenless connection probes (issue #99). Behavior in auth=none mode is unchanged.
            if (string.IsNullOrEmpty(token)
                && _strategy.AuthOption == Consts.MCP.Server.AuthOption.required)
            {
                _connectionRejected = true;
                _logger.LogDebug(
                    "{guid} MCP Plugin connection rejected (auth=required, empty token) — webhook skipped. ConnectionId: {connectionId}.",
                    _guid, Context.ConnectionId);

                try
                {
                    await Clients.Caller.ForceDisconnect("Authorization failed. A bearer token is required in auth=required mode.");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "{guid} Failed to send ForceDisconnect notification for rejected connection. ConnectionId: {connectionId}.",
                        _guid, Context.ConnectionId);
                }

                Context.Abort();
                return;
            }

            // Check authorization webhook
            var allowed = await _authorizationWebhookService.AuthorizePluginAsync(
                connectionId: Context.ConnectionId,
                bearerToken: token,
                clientName: null,
                clientVersion: null,
                cancellationToken: Context.ConnectionAborted);

            if (!allowed)
            {
                _connectionRejected = true;
                _logger.LogDebug("{guid} MCP Plugin connection rejected by authorization webhook. ConnectionId: {connectionId}.",
                    _guid, Context.ConnectionId);

                // Notify the client about the rejection reason before closing.
                // This allows the client to take specific action (e.g. clear cached tokens).
                try
                {
                    await Clients.Caller.ForceDisconnect("Authorization failed. Token may be missing, invalid, or revoked.");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "{guid} Failed to send ForceDisconnect notification for rejected connection. ConnectionId: {connectionId}.",
                        _guid, Context.ConnectionId);
                }

                // Context.Abort() signals SignalR to tear down the connection.
                // However, the rest of OnConnectedAsync (and overrides in derived classes)
                // may still execute before SignalR processes the abort. The _connectionRejected
                // flag acts as a guard so derived hubs can skip their post-connection logic.
                // SignalR will eventually invoke OnDisconnectedAsync — derived classes should
                // also check _connectionRejected there to skip cleanup for never-established connections.
                Context.Abort();
                return;
            }

            _strategy.OnPluginConnected(GetType(), Context.ConnectionId, token, _logger,
                (id, reason) =>
                {
                    var client = Clients.Client(id);
                    if (client == null)
                    {
                        _logger.LogWarning("{guid} ForceDisconnect skipped: no SignalR connection found for ConnectionId: {connectionId}. Client may have already disconnected.", _guid, id);
                        return;
                    }
                    client.ForceDisconnect(reason);
                });

            _logger.LogDebug("{guid} MCP Plugin connected. ConnectionId: {connectionId}, Token: {hasToken}.",
                _guid, Context.ConnectionId, !string.IsNullOrEmpty(token) ? "present" : "absent");
            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionRejected)
            {
                _logger.LogDebug("{guid} MCP Plugin disconnected (rejected connection, skipping cleanup). ConnectionId: {connectionId}.", _guid, Context.ConnectionId);
                return Task.CompletedTask;
            }

            _logger.LogDebug("{guid} MCP Plugin disconnected. ConnectionId: {connectionId}.", _guid, Context.ConnectionId);
            _strategy.OnPluginDisconnected(GetType(), Context.ConnectionId, _logger);
            return base.OnDisconnectedAsync(exception);
        }

        public virtual new void Dispose()
        {
            _logger.LogTrace("Dispose. {0}", _guid);
            base.Dispose();
            _disposables.Dispose();
        }
    }
}
