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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
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
        readonly Common.Version _version;
        readonly IDataArguments _dataArguments;
        readonly CompositeDisposable _disposables = new();
        string _sessionId = "unknown";

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
            await _hubContext.Clients.All.OnMcpClientConnected(GetClientData());
        }

        public async Task NotifyClientDisconnectedAsync()
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(NotifyClientDisconnectedAsync));
            await _hubContext.Clients.All.OnMcpClientDisconnected();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(StartAsync));
            _logger.LogDebug("{type} MCP Client connected. SessionId: {sessionId}, ClientName: {clientName}, ClientTitle: {clientTitle}.",
                GetType().GetTypeShortName(), McpSessionOrServer?.SessionId, _mcpServer?.ClientInfo?.Name, _mcpServer?.ClientInfo?.Title);
            _disposables.Clear();

            _eventAppToolsChange
                .Subscribe(data =>
                {
                    _logger.LogTrace("{type} EventAppToolsChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListToolUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _eventAppPromptsChange
                .Subscribe(data =>
                {
                    _logger.LogTrace("{type} EventAppPromptsChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListPromptsUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _eventAppResourcesChange
                .Subscribe(data =>
                {
                    _logger.LogTrace("{type} EventAppResourcesChange. ConnectionId: {connectionId}", GetType().GetTypeShortName(), data.ConnectionId);
                    OnListResourcesUpdated(data, cancellationToken);
                })
                .AddTo(_disposables);

            _sessionId = McpSessionTokenContext.CurrentToken
                      ?? McpSessionOrServer?.SessionId
                      ?? Common.Consts.MCP.Server.TransportMethod.stdio.ToString();
            _sessionTracker.Update(_sessionId, GetClientData(), GetServerData());
            _logger.LogDebug("{type} Session tracked. Key: {sessionId}.", GetType().GetTypeShortName(), _sessionId);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000, cancellationToken); // Wait a bit to ensure connection is fully established

                    // Update session tracker with fresh data after MCP initialize handshake has completed
                    _sessionTracker.Update(_sessionId, GetClientData(), GetServerData());

                    await NotifyClientConnectedAsync();
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
            _logger.LogDebug("{type} MCP Client disconnected. SessionId: {sessionId}.", GetType().GetTypeShortName(), _sessionId);

            try
            {
                await NotifyClientDisconnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{type} Error notifying client disconnected.", GetType().GetTypeShortName());
            }

            _disposables.Clear();
            _sessionTracker.Remove(_sessionId);
            _logger.LogDebug("{type} Session removed. Key: {sessionId}.", GetType().GetTypeShortName(), _sessionId);
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
    }
}
