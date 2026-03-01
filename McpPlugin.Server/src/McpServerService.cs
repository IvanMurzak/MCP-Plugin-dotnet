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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpServerService : IHostedService
    {
        readonly ILogger<McpServerService> _logger;
        readonly McpServer? _mcpServer;
        readonly McpSession? _mcpSession; // Should be replaced with McpSession class, but for now it doesn't work in csharp-sdk.0.4.1-preview.1
        readonly HubEventToolsChange _eventAppToolsChange;
        readonly HubEventPromptsChange _eventAppPromptsChange;
        readonly HubEventResourcesChange _eventAppResourcesChange;
        readonly IHubContext<McpServerHub, IClientMcpRpc> _hubContext;
        readonly IMcpSessionTracker _sessionTracker;
        readonly IMcpConnectionStrategy _strategy;
        readonly Common.Version _version;
        readonly IDataArguments _dataArguments;
        readonly IWebhookEventCollector _webhookCollector;
        readonly CompositeDisposable _disposables = new();

        // _physicalSessionId: unique per HTTP/stdio connection (MCP protocol session UUID).
        //   Used as the tracker key and ref-count key so every physical connection
        //   gets its own entry, even when multiple clients share the same Bearer token.
        // _routingToken: the Bearer token from the HTTP Authorization header (or configured
        //   token for stdio auth=required). Used to route SignalR notifications to the correct
        //   plugin and to filter GetAllClientData() calls. Null in no-auth mode.
        string _physicalSessionId = "unknown";
        string? _routingToken = null;

        public McpSession? McpSessionOrServer => _mcpSession ?? _mcpServer;

        public McpServerService(
            ILogger<McpServerService> logger,
            Common.Version version,
            IDataArguments dataArguments,
            HubEventToolsChange eventAppToolsChange,
            HubEventPromptsChange eventAppPromptsChange,
            HubEventResourcesChange eventAppResourcesChange,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext,
            IMcpSessionTracker sessionTracker,
            IMcpConnectionStrategy strategy,
            IWebhookEventCollector webhookCollector,
            McpServer? mcpServer = null,
            McpSession? mcpSession = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{type} Ctor.", GetType().GetTypeShortName());
            _mcpServer = mcpServer;
            _mcpSession = mcpSession;

            if (_mcpSession == null && _mcpServer == null)
                throw new InvalidOperationException($"{nameof(mcpSession)} and {nameof(mcpServer)} are both null.");

            _version = version ?? throw new ArgumentNullException(nameof(version));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _eventAppToolsChange = eventAppToolsChange ?? throw new ArgumentNullException(nameof(eventAppToolsChange));
            _eventAppPromptsChange = eventAppPromptsChange ?? throw new ArgumentNullException(nameof(eventAppPromptsChange));
            _eventAppResourcesChange = eventAppResourcesChange ?? throw new ArgumentNullException(nameof(eventAppResourcesChange));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _webhookCollector = webhookCollector ?? throw new ArgumentNullException(nameof(webhookCollector));
        }

        public McpClientData GetClientData()
        {
            return new McpClientData
            {
                IsConnected = _mcpServer?.ClientInfo != null,
                SessionId = McpSessionOrServer?.SessionId,
                ClientTitle = _mcpServer?.ClientInfo?.Title,
                ClientName = _mcpServer?.ClientInfo?.Name,
                ClientVersion = _mcpServer?.ClientInfo?.Version,
                ClientDescription = _mcpServer?.ClientInfo?.Description,
                ClientWebsiteUrl = _mcpServer?.ClientInfo?.WebsiteUrl
            };
        }

        public McpServerData GetServerData()
        {
            return new McpServerData
            {
                IsAiAgentConnected = _mcpServer?.ClientInfo != null,
                ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                ServerApiVersion = _version.Api,
                ServerTransport = _dataArguments.ClientTransport
            };
        }

        public async Task NotifyClientConnectedAsync()
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(NotifyClientConnectedAsync));
            var connectedClient = GetClientData();
            // Filter to clients visible to this plugin's routing token group.
            // Null token (no-auth) → returns all sessions; specific token → returns only matching.
            var allActiveClients = _sessionTracker.GetAllClientData(_routingToken).ToArray();
            var connectionId = ClientUtils.GetConnectionIdByToken(_routingToken);
            if (connectionId != null)
                await _hubContext.Clients.Client(connectionId).OnMcpClientConnected(connectedClient, allActiveClients);
            else
                await _hubContext.Clients.All.OnMcpClientConnected(connectedClient, allActiveClients);
        }

        public async Task NotifyClientDisconnectedAsync()
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(NotifyClientDisconnectedAsync));
            // The physical session was already removed from the tracker by StopAsync before this call,
            // so GetAllClientData() returns the remaining clients (this session excluded).
            var disconnectedClient = GetClientData();
            var remainingClients = _sessionTracker.GetAllClientData(_routingToken).ToArray();
            var connectionId = ClientUtils.GetConnectionIdByToken(_routingToken);
            if (connectionId != null)
                await _hubContext.Clients.Client(connectionId).OnMcpClientDisconnected(disconnectedClient, remainingClients);
            else
                await _hubContext.Clients.All.OnMcpClientDisconnected(disconnectedClient, remainingClients);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(StartAsync));
            _logger.LogDebug("{type} MCP Client connected. SessionId: {sessionId}, ClientName: {clientName}, ClientTitle: {clientTitle}.",
                GetType().GetTypeShortName(), McpSessionOrServer?.SessionId, _mcpServer?.ClientInfo?.Name, _mcpServer?.ClientInfo?.Title);
            _disposables.Clear();

            // _routingToken: Bearer token used to route notifications to the correct plugin and to
            // scope GetAllClientData() queries. Null in no-auth mode (broadcasts to all plugins).
            // For auth=required on stdio (no HTTP context), fall back to dataArguments.Token so
            // the routing key matches what the plugin registered with.
            _routingToken = McpSessionTokenContext.CurrentToken
                         ?? (_strategy.AuthOption == Common.Consts.MCP.Server.AuthOption.required
                             ? _dataArguments.Token
                             : null);

            // _physicalSessionId: always unique per HTTP session (MCP protocol UUID) so each
            // physical connection gets its own tracker entry regardless of shared tokens.
            // Fall back to the routing token (stdio) or a generated ID as a last resort.
            _physicalSessionId = McpSessionOrServer?.SessionId
                              ?? _routingToken
                              ?? Common.Consts.MCP.Server.TransportMethod.stdio.ToString();

            _sessionTracker.Update(_physicalSessionId, _routingToken, GetClientData(), GetServerData());
            _sessionTracker.AddRef(_physicalSessionId);
            _logger.LogDebug("{type} Session tracked. PhysicalId: {physicalId}, RoutingToken: {hasToken}.",
                GetType().GetTypeShortName(), _physicalSessionId, _routingToken != null ? "present" : "absent");

            // ShouldNotifySession compares the plugin's token with the provided session key.
            // Use _routingToken when available; fall back to _physicalSessionId for no-auth mode
            // (NoAuthMcpStrategy.ShouldNotifySession ignores the value and always returns true).
            var notifySessionKey = _routingToken ?? _physicalSessionId;

            _eventAppToolsChange
                .Subscribe(data =>
                {
                    if (!_strategy.ShouldNotifySession(data.ConnectionId, notifySessionKey))
                        return;
                    _logger.LogTrace("{type} EventAppToolsChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListToolUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _eventAppPromptsChange
                .Subscribe(data =>
                {
                    if (!_strategy.ShouldNotifySession(data.ConnectionId, notifySessionKey))
                        return;
                    _logger.LogTrace("{type} EventAppPromptsChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListPromptsUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _eventAppResourcesChange
                .Subscribe(data =>
                {
                    if (!_strategy.ShouldNotifySession(data.ConnectionId, notifySessionKey))
                        return;
                    _logger.LogTrace("{type} EventAppResourcesChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListResourcesUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000, cancellationToken); // Wait a bit to ensure connection is fully established

                    // Update session tracker with fresh data after MCP initialize handshake has completed
                    _sessionTracker.Update(_physicalSessionId, _routingToken, GetClientData(), GetServerData());

                    await NotifyClientConnectedAsync();

                    // Emit AI agent connected webhook
                    var clientInfo = _mcpServer?.ClientInfo;
                    var metadata = BuildAiAgentMetadata(clientInfo);
                    _webhookCollector.OnAiAgentConnected(
                        _physicalSessionId,
                        clientInfo?.Name,
                        clientInfo?.Version,
                        metadata);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("{type} {method} was cancelled.",
                        GetType().GetTypeShortName(), nameof(NotifyClientConnectedAsync));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying client connected.");
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(StopAsync));
            _logger.LogDebug("{type} MCP Client disconnected. PhysicalId: {physicalId}.", GetType().GetTypeShortName(), _physicalSessionId);

            _disposables.Clear();

            _webhookCollector.OnAiAgentDisconnected(_physicalSessionId);

            var isLastConnection = _sessionTracker.Remove(_physicalSessionId);
            if (isLastConnection)
            {
                _logger.LogDebug("{type} Last connection for session ended, notifying plugin. PhysicalId: {physicalId}.",
                    GetType().GetTypeShortName(), _physicalSessionId);
                try
                {
                    await NotifyClientDisconnectedAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{type} Error notifying client disconnected.", GetType().GetTypeShortName());
                }
            }
            else
            {
                _logger.LogDebug("{type} Session still has active connections, skipping disconnect notification. PhysicalId: {physicalId}.",
                    GetType().GetTypeShortName(), _physicalSessionId);
            }
        }

        async void OnListToolUpdated(HubEventToolsChange.EventData eventData, CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}", GetType().GetTypeShortName(), nameof(OnListToolUpdated));
            try
            {
                if (McpSessionOrServer == null)
                {
                    _logger.LogDebug("{type} {property} is null, cannot send tool list update notification.",
                        GetType().GetTypeShortName(), nameof(McpSessionOrServer));
                    return;
                }
#pragma warning disable CS0618 // Type or member is obsolete
                await McpSessionOrServer.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            catch (Exception ex)
            {
                _logger.LogError("{type} Error updating tools: {Message}", GetType().GetTypeShortName(), ex.Message);
            }
        }
        async void OnResourceUpdated(HubEventToolsChange.EventData eventData, CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}", GetType().GetTypeShortName(), nameof(OnResourceUpdated));
            try
            {
                if (McpSessionOrServer == null)
                {
                    _logger.LogDebug("{type} {property} is null, cannot send resource update notification.",
                        GetType().GetTypeShortName(), nameof(McpSessionOrServer));
                    return;
                }
#pragma warning disable CS0618 // Type or member is obsolete
                await McpSessionOrServer.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            catch (Exception ex)
            {
                _logger.LogError("{type} Error updating resource: {Message}", GetType().GetTypeShortName(), ex.Message);
            }
        }
        async void OnListPromptsUpdated(HubEventPromptsChange.EventData eventData, CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}", GetType().GetTypeShortName(), nameof(OnListPromptsUpdated));
            try
            {
                if (McpSessionOrServer == null)
                {
                    _logger.LogDebug("{type} {property} is null, cannot send prompt list update notification.",
                        GetType().GetTypeShortName(), nameof(McpSessionOrServer));
                    return;
                }
#pragma warning disable CS0618 // Type or member is obsolete
                await McpSessionOrServer.SendNotificationAsync(NotificationMethods.PromptListChangedNotification, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            catch (Exception ex)
            {
                _logger.LogError("{type} Error updating prompts: {Message}", GetType().GetTypeShortName(), ex.Message);
            }
        }
        async void OnListResourcesUpdated(HubEventResourcesChange.EventData eventData, CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}", GetType().GetTypeShortName(), nameof(OnListResourcesUpdated));
            try
            {
                if (McpSessionOrServer == null)
                {
                    _logger.LogDebug("{type} {property} is null, cannot send resource list update notification.",
                        GetType().GetTypeShortName(), nameof(McpSessionOrServer));
                    return;
                }
#pragma warning disable CS0618 // Type or member is obsolete
                await McpSessionOrServer.SendNotificationAsync(NotificationMethods.ResourceListChangedNotification, cancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            catch (Exception ex)
            {
                _logger.LogError("{type} Error updating resource list: {Message}", GetType().GetTypeShortName(), ex.Message);
            }
        }

        static Dictionary<string, string>? BuildAiAgentMetadata(ModelContextProtocol.Protocol.Implementation? clientInfo)
        {
            if (clientInfo == null)
                return null;

            var metadata = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(clientInfo.Title))
                metadata["title"] = clientInfo.Title;

            if (!string.IsNullOrEmpty(clientInfo.Description))
                metadata["description"] = clientInfo.Description;

            if (!string.IsNullOrEmpty(clientInfo.WebsiteUrl))
                metadata["websiteUrl"] = clientInfo.WebsiteUrl;

            return metadata.Count > 0 ? metadata : null;
        }
    }
}
