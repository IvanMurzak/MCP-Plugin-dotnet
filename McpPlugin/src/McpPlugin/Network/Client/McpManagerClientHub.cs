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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin
{
    public class McpManagerClientHub : BaseHubConnector, IRemoteMcpManagerHub
    {
        readonly IClientMcpManager _mcpManager;

        public McpManagerClientHub(
            ILogger<McpManagerClientHub> logger,
            Version apiVersion,
            IHubConnectionProvider hubConnectionProvider,
            IClientMcpManager mcpManager)
            : base(
                logger: logger,
                apiVersion: apiVersion,
                endpoint: Consts.Hub.RemoteApp,
                hubConnectionProvider: hubConnectionProvider)
        {
            _mcpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
        }

        #region Client Events

        protected override void SubscribeOnServerEvents(HubConnection hubConnection, CompositeDisposable disposables)
        {
            hubConnection.On<McpClientData>(nameof(IClientMcpRpc.OnMcpClientConnected), data =>
            {
                _logger.LogDebug("{class}.{method}", nameof(IClientMcpManager), nameof(IClientMcpManager.OnMcpClientConnected));
                return _mcpManager.OnMcpClientConnected(data);
            })
            .AddTo(_serverEventsDisposables);

            hubConnection.On(nameof(IClientMcpRpc.ForceDisconnect), async () =>
            {
                _logger.LogDebug("{class}.{method}", nameof(IClientMcpManager), nameof(IClientMcpManager.ForceDisconnect));
                await _mcpManager.ForceDisconnect();
                await _connectionManager.Disconnect();
            });

            // Tool events -------------------------------------------------------------

            if (_mcpManager.ToolHub != null)
            {
                hubConnection.On<RequestCallTool, ResponseData<ResponseCallTool>>(nameof(IClientToolHub.RunCallTool), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientToolHub), nameof(IClientToolHub.RunCallTool));
                        return _mcpManager.ToolHub.RunCallTool(data);
                    })
                    .AddTo(_serverEventsDisposables);

                hubConnection.On<RequestListTool, ResponseData<ResponseListTool[]>>(nameof(IClientToolHub.RunListTool), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientToolHub), nameof(IClientToolHub.RunListTool));
                        return _mcpManager.ToolHub.RunListTool(data);
                    })
                    .AddTo(_serverEventsDisposables);
            }

            // Prompt events -----------------------------------------------------------

            if (_mcpManager.PromptHub != null)
            {
                hubConnection.On<RequestGetPrompt, ResponseData<ResponseGetPrompt>>(nameof(IClientPromptHub.RunGetPrompt), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientPromptHub), nameof(IClientPromptHub.RunGetPrompt));
                        return _mcpManager.PromptHub.RunGetPrompt(data);
                    })
                    .AddTo(_serverEventsDisposables);

                hubConnection.On<RequestListPrompts, ResponseData<ResponseListPrompts>>(nameof(IClientPromptHub.RunListPrompts), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientPromptHub), nameof(IClientPromptHub.RunListPrompts));
                        return _mcpManager.PromptHub.RunListPrompts(data);
                    })
                    .AddTo(_serverEventsDisposables);
            }

            // Resource events ---------------------------------------------------------

            if (_mcpManager.ResourceHub != null)
            {
                hubConnection.On<RequestResourceContent, ResponseData<ResponseResourceContent[]>>(nameof(IClientResourceHub.RunResourceContent), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunResourceContent));
                        return _mcpManager.ResourceHub.RunResourceContent(data);
                    })
                    .AddTo(_serverEventsDisposables);

                hubConnection.On<RequestListResources, ResponseData<ResponseListResource[]>>(nameof(IClientResourceHub.RunListResources), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunListResources));
                        return _mcpManager.ResourceHub.RunListResources(data);
                    })
                    .AddTo(_serverEventsDisposables);

                hubConnection.On<RequestListResourceTemplates, ResponseData<ResponseResourceTemplate[]>>(nameof(IClientResourceHub.RunResourceTemplates), data =>
                    {
                        _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunResourceTemplates));
                        return _mcpManager.ResourceHub.RunResourceTemplates(data);
                    })
                    .AddTo(_serverEventsDisposables);
            }
        }

        #endregion

        #region Server Calls

        public Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request) => NotifyAboutUpdatedTools(request, _cancellationTokenSource.Token);
        public Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedTools));
            return _connectionManager.InvokeAsync<RequestToolsUpdated, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedTools), request, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request) => NotifyAboutUpdatedPrompts(request, _cancellationTokenSource.Token);
        public Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedPrompts));
            return _connectionManager.InvokeAsync<RequestPromptsUpdated, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedPrompts), request, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedResources(RequestResourcesUpdated request) => NotifyAboutUpdatedResources(request, _cancellationTokenSource.Token);
        public Task<ResponseData> NotifyAboutUpdatedResources(RequestResourcesUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedResources));
            return _connectionManager.InvokeAsync<RequestResourcesUpdated, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedResources), request, cancellationToken);
        }

        public Task<ResponseData> NotifyToolRequestCompleted(RequestToolCompletedData request) => NotifyToolRequestCompleted(request, _cancellationTokenSource.Token);
        public Task<ResponseData> NotifyToolRequestCompleted(RequestToolCompletedData request, CancellationToken cancellationToken = default)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("{class}.{method} request: {RequestId}\n{Json}",
                    nameof(IServerMcpManager),
                    nameof(IServerMcpManager.NotifyToolRequestCompleted),
                    request.RequestId,
                    request.ToPrettyJson()
                );
            }
            return _connectionManager.InvokeAsync<RequestToolCompletedData, ResponseData>(nameof(IServerMcpManager.NotifyToolRequestCompleted), request, cancellationToken);
        }

        public Task<McpClientData> GetMcpClientData()
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.GetMcpClientData));
            return _connectionManager.InvokeAsync<McpClientData>(nameof(IServerMcpManager.GetMcpClientData), _cancellationTokenSource.Token);
        }

        #endregion
    }
}
