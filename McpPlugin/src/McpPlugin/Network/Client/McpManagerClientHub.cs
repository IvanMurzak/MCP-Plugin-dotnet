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
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Common
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
            hubConnection.On(nameof(IClientMcpManager.ForceDisconnect), async () =>
            {
                _logger.LogDebug("{class}.{method}", nameof(IClientMcpManager), nameof(IClientMcpManager.ForceDisconnect));
                _mcpManager.ForceDisconnect();
                await _connectionManager.Disconnect();
            });

            // Tool events -------------------------------------------------------------

            hubConnection.On<RequestCallTool, ResponseData<ResponseCallTool>>(nameof(IClientToolHub.RunCallTool), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientToolHub), nameof(IClientToolHub.RunCallTool));
                    return await _mcpManager.ToolHub.RunCallTool(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListTool, ResponseData<ResponseListTool[]>>(nameof(IClientToolHub.RunListTool), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientToolHub), nameof(IClientToolHub.RunListTool));
                    return await _mcpManager.ToolHub.RunListTool(data);
                })
                .AddTo(_serverEventsDisposables);

            // Prompt events -----------------------------------------------------------

            hubConnection.On<RequestGetPrompt, ResponseData<ResponseGetPrompt>>(nameof(IClientPromptHub.RunGetPrompt), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientPromptHub), nameof(IClientPromptHub.RunGetPrompt));
                    return await _mcpManager.PromptHub.RunGetPrompt(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListPrompts, ResponseData<ResponseListPrompts>>(nameof(IClientPromptHub.RunListPrompts), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientPromptHub), nameof(IClientPromptHub.RunListPrompts));
                    return await _mcpManager.PromptHub.RunListPrompts(data);
                })
                .AddTo(_serverEventsDisposables);

            // Resource events ---------------------------------------------------------

            hubConnection.On<RequestResourceContent, ResponseData<ResponseResourceContent[]>>(nameof(IClientResourceHub.RunResourceContent), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunResourceContent));
                    return await _mcpManager.ResourceHub.RunResourceContent(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListResources, ResponseData<ResponseListResource[]>>(nameof(IClientResourceHub.RunListResources), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunListResources));
                    return await _mcpManager.ResourceHub.RunListResources(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListResourceTemplates, ResponseData<ResponseResourceTemplate[]>>(nameof(IClientResourceHub.RunResourceTemplates), async data =>
                {
                    _logger.LogDebug("{class}.{method}", nameof(IClientResourceHub), nameof(IClientResourceHub.RunResourceTemplates));
                    return await _mcpManager.ResourceHub.RunResourceTemplates(data);
                })
                .AddTo(_serverEventsDisposables);
        }

        #endregion

        #region Server Calls

        public Task<ResponseData> NotifyAboutUpdatedTools(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedTools));
            return _connectionManager.InvokeAsync<string, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedTools), data, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedPrompts));
            return _connectionManager.InvokeAsync<string, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedPrompts), data, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedResources(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class}.{method}", nameof(IServerMcpManager), nameof(IServerMcpManager.NotifyAboutUpdatedResources));
            return _connectionManager.InvokeAsync<string, ResponseData>(nameof(IServerMcpManager.NotifyAboutUpdatedResources), data, cancellationToken);
        }

        public Task<ResponseData> NotifyToolRequestCompleted(ResponseCallTool response, CancellationToken cancellationToken = default)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("{class}.{method} request: {RequestID}\n{Json}",
                    nameof(IServerMcpManager),
                    nameof(IServerMcpManager.NotifyToolRequestCompleted),
                    response.RequestID,
                    response.ToPrettyJson()
                );
            }
            var data = new ToolRequestCompletedData
            {
                RequestId = response.RequestID,
                Result = response
            };
            return _connectionManager.InvokeAsync<ToolRequestCompletedData, ResponseData>(nameof(IServerMcpManager.NotifyToolRequestCompleted), data, cancellationToken);
        }

        #endregion
    }
}
