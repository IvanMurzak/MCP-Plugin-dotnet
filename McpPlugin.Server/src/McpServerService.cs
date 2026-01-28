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
        readonly IClientToolHub _toolRunner;
        readonly IClientPromptHub _promptRunner;
        readonly IClientResourceHub _resourceRunner;
        readonly HubEventToolsChange _eventAppToolsChange;
        readonly HubEventPromptsChange _eventAppPromptsChange;
        readonly HubEventResourcesChange _eventAppResourcesChange;
        readonly IHubContext<McpServerHub, IClientMcpRpc> _hubContext;
        readonly CompositeDisposable _disposables = new();

        public McpSession McpSessionOrServer => _mcpSession ?? _mcpServer ?? throw new InvalidOperationException($"{nameof(_mcpSession)} and {nameof(_mcpServer)} are both null.");

        public IClientToolHub ToolRunner => _toolRunner;
        public IClientPromptHub PromptRunner => _promptRunner;
        public IClientResourceHub ResourceRunner => _resourceRunner;

        public static McpServerService? Instance { get; private set; }

        public McpServerService(
            ILogger<McpServerService> logger,
            IClientToolHub toolRunner,
            IClientPromptHub promptRunner,
            IClientResourceHub resourceRunner,
            HubEventToolsChange eventAppToolsChange,
            HubEventPromptsChange eventAppPromptsChange,
            HubEventResourcesChange eventAppResourcesChange,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext,
            McpServer? mcpServer = null,
            McpSession? mcpSession = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{0} Ctor.", GetType().GetTypeShortName());
            _mcpServer = mcpServer;
            _mcpSession = mcpSession;

            if (_mcpSession == null && _mcpServer == null)
                throw new InvalidOperationException($"{nameof(mcpSession)} and {nameof(mcpServer)} are both null.");

            _toolRunner = toolRunner ?? throw new ArgumentNullException(nameof(toolRunner));
            _promptRunner = promptRunner ?? throw new ArgumentNullException(nameof(promptRunner));
            _resourceRunner = resourceRunner ?? throw new ArgumentNullException(nameof(resourceRunner));
            _eventAppToolsChange = eventAppToolsChange ?? throw new ArgumentNullException(nameof(eventAppToolsChange));
            _eventAppPromptsChange = eventAppPromptsChange ?? throw new ArgumentNullException(nameof(eventAppPromptsChange));
            _eventAppResourcesChange = eventAppResourcesChange ?? throw new ArgumentNullException(nameof(eventAppResourcesChange));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));

            // if (Instance != null)
            //     throw new InvalidOperationException($"{typeof(McpServerService).Name} is already initialized.");
            Instance = this;
        }

        public McpClientData GetClientData()
        {
            // var session = McpSessionOrServer;
            return new McpClientData
            {
                IsConnected = true,
                ClientName = "Unknown", // session.ClientInfo?.Name,
                ClientVersion = "Unknown" // session.ClientInfo?.Version
            };
        }

        public async Task NotifyClientConnectedAsync()
        {
            await _hubContext.Clients.All.OnMcpClientConnected(GetClientData());
        }

        public async Task NotifyClientDisconnectedAsync()
        {
            await _hubContext.Clients.All.OnMcpClientDisconnected();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(StartAsync));
            _disposables.Clear();

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000, cancellationToken);
                    await NotifyClientConnectedAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error notifying client connected.");
                }
            }, cancellationToken);

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

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}.", GetType().GetTypeShortName(), nameof(StopAsync));

            await NotifyClientDisconnectedAsync();

            _disposables.Clear();
            if (Instance == this)
                Instance = null;
        }

        async void OnListToolUpdated(HubEventToolsChange.EventData eventData, CancellationToken cancellationToken)
        {
            _logger.LogTrace("{type} {method}", GetType().GetTypeShortName(), nameof(OnListToolUpdated));
            try
            {
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
