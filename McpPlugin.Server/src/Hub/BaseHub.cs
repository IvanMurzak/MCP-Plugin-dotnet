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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class BaseHub<T> : Hub<T>, IDisposable
        where T : class, IClientDisconnectable
    {
        protected readonly ILogger _logger;
        // protected readonly IHubContext<T> _hubContext;
        protected readonly CompositeDisposable _disposables = new();
        // protected readonly CancellationTokenSource _cancellationTokenSource = new();
        protected readonly string _guid = Guid.NewGuid().ToString();

        protected BaseHub(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor. {guid}", _guid);
            // _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        public override Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var token = httpContext?.Request.Query["access_token"].FirstOrDefault();
            if (string.IsNullOrEmpty(token))
            {
                var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
                if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = authHeader.Substring("Bearer ".Length).Trim();
            }

            ClientUtils.AddClient(GetType(), Context.ConnectionId, _logger, token);
            _logger.LogDebug("{guid} MCP Plugin connected. ConnectionId: {connectionId}, Token: {hasToken}.",
                _guid, Context.ConnectionId, !string.IsNullOrEmpty(token) ? "present" : "absent");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug("{guid} MCP Plugin disconnected. ConnectionId: {connectionId}.", _guid, Context.ConnectionId);
            ClientUtils.RemoveClient(GetType(), Context.ConnectionId, _logger);
            return base.OnDisconnectedAsync(exception);
        }

        protected virtual void DisconnectOtherClients(ConcurrentDictionary<string, bool> clients, string currentConnectionId)
        {
            if (clients.IsEmpty)
                return;

            foreach (var connectionId in clients.Keys.Where(c => c != currentConnectionId).ToList())
            {
                if (clients.TryRemove(connectionId, out _))
                {
                    _logger.LogInformation("{0} Client '{1}' removed from connected clients for {2}.", _guid, connectionId, GetType().GetTypeShortName());
                    var client = Clients.Client(connectionId);
                    if (client == null)
                    {
                        _logger.LogWarning("{0} Client '{1}' not found in connected clients for {2}.", _guid, connectionId, GetType().GetTypeShortName());
                        continue;
                    }
                    client.ForceDisconnect();
                }
                else
                {
                    _logger.LogWarning("{0} Client '{1}' was not found in connected clients for {2}.", _guid, connectionId, GetType().GetTypeShortName());
                }
            }
        }

        public virtual new void Dispose()
        {
            _logger.LogTrace("Dispose. {0}", _guid);
            base.Dispose();
            _disposables.Dispose();
        }
    }
}
