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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Common
{
    public abstract class ClientHub : IClientHub
    {
        protected readonly ILogger _logger;
        protected readonly Version _apiVersion;
        protected readonly IMcpManager _mcpManager;
        protected readonly IConnectionManager _connectionManager;
        protected readonly IDisposable _hubConnectionDisposable;
        protected readonly CompositeDisposable _serverEventsDisposables = new();

        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _connectionManager.ConnectionState;
        public ReadOnlyReactiveProperty<bool> KeepConnected => _connectionManager.KeepConnected;

        public ClientHub(ILogger logger, Version apiVersion, IConnectionManager connectionManager, IMcpManager mcpManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class} Ctor.", nameof(RemoteServerHub));

            _apiVersion = apiVersion ?? throw new ArgumentNullException(nameof(apiVersion));
            _mcpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

            _connectionManager.Endpoint = Consts.Hub.RemoteApp;

            _hubConnectionDisposable = connectionManager.HubConnection
                .Subscribe(SubscribeOnServerEvents);
        }

        public Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Connecting... (to RemoteApp: {endpoint}).", GetType().Name, _connectionManager.Endpoint);
            return _connectionManager.Connect(cancellationToken);
        }
        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Disconnecting... (to RemoteApp: {endpoint}).", GetType().Name, _connectionManager.Endpoint);
            return _connectionManager.Disconnect(cancellationToken);
        }

        void SubscribeOnServerEvents(HubConnection? hubConnection)
        {
            _logger.LogTrace("{class} Clearing server events disposables.", GetType().Name);
            _serverEventsDisposables.Clear();

            if (hubConnection == null)
                return;

            _logger.LogTrace("{class} Subscribing to server events.", GetType().Name);

            hubConnection.On(Consts.RPC.Client.ForceDisconnect, async () =>
            {
                _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.ForceDisconnect);
                await _connectionManager.Disconnect();
            });

            // Tool events -------------------------------------------------------------

            hubConnection.On<RequestCallTool, ResponseData<ResponseCallTool>>(Consts.RPC.Client.RunCallTool, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunCallTool);
                    return await _mcpManager.RunCallTool(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListTool, ResponseData<ResponseListTool[]>>(Consts.RPC.Client.RunListTool, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunListTool);
                    return await _mcpManager.RunListTool(data);
                })
                .AddTo(_serverEventsDisposables);

            // Prompt events -----------------------------------------------------------

            hubConnection.On<RequestGetPrompt, ResponseData<ResponseGetPrompt>>(Consts.RPC.Client.RunGetPrompt, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunGetPrompt);
                    return await _mcpManager.RunGetPrompt(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListPrompts, ResponseData<ResponseListPrompts>>(Consts.RPC.Client.RunListPrompts, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunListPrompts);
                    return await _mcpManager.RunListPrompts(data);
                })
                .AddTo(_serverEventsDisposables);

            // Resource events ---------------------------------------------------------

            hubConnection.On<RequestResourceContent, ResponseData<ResponseResourceContent[]>>(Consts.RPC.Client.RunResourceContent, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunResourceContent);
                    return await _mcpManager.RunResourceContent(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListResources, ResponseData<ResponseListResource[]>>(Consts.RPC.Client.RunListResources, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunListResources);
                    return await _mcpManager.RunListResources(data);
                })
                .AddTo(_serverEventsDisposables);

            hubConnection.On<RequestListResourceTemplates, ResponseData<ResponseResourceTemplate[]>>(Consts.RPC.Client.RunListResourceTemplates, async data =>
                {
                    _logger.LogDebug("{class}.{method}", GetType().Name, Consts.RPC.Client.RunListResourceTemplates);
                    return await _mcpManager.RunResourceTemplates(data);
                })
                .AddTo(_serverEventsDisposables);
        }

        public Task<ResponseData> NotifyAboutUpdatedTools(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Notify server about updated tools.", GetType().Name);
            return _connectionManager.InvokeAsync<string, ResponseData>(Consts.RPC.Server.OnListToolsUpdated, string.Empty, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Notify server about updated prompts.", GetType().Name);
            return _connectionManager.InvokeAsync<string, ResponseData>(Consts.RPC.Server.OnListPromptsUpdated, string.Empty, cancellationToken);
        }

        public Task<ResponseData> NotifyAboutUpdatedResources(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Notify server about updated resources.", GetType().Name);
            return _connectionManager.InvokeAsync<string, ResponseData>(Consts.RPC.Server.OnListResourcesUpdated, string.Empty, cancellationToken);
        }

        public Task<ResponseData> NotifyToolRequestCompleted(ResponseCallTool response, CancellationToken cancellationToken = default)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("{class} Notify tool request completed for request: {RequestID}\n{Json}",
                    GetType().Name,
                    response.RequestID,
                    response.ToPrettyJson()
                );
            }
            var data = new ToolRequestCompletedData
            {
                RequestId = response.RequestID,
                Result = response
            };
            return _connectionManager.InvokeAsync<ToolRequestCompletedData, ResponseData>(Consts.RPC.Server.OnToolRequestCompleted, data, cancellationToken);
        }

        public async Task<VersionHandshakeResponse?> PerformVersionHandshake(CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{class} Performing version handshake.", GetType().Name);

            var request = new VersionHandshakeRequest
            {
                RequestID = Guid.NewGuid().ToString(),
                ApiVersion = _apiVersion.Api,
                PluginVersion = _apiVersion.Plugin,
                UnityVersion = _apiVersion.UnityVersion
            };

            try
            {
                var response = await _connectionManager.InvokeAsync<VersionHandshakeRequest, VersionHandshakeResponse>(
                    Consts.RPC.Server.OnVersionHandshake, request, cancellationToken);

                if (response == null)
                {
                    _logger.LogError("{class} Version handshake failed: No response from server.", GetType().Name);
                    return null;
                }

                _logger.LogInformation("{class} Version handshake completed. Compatible: {Compatible}, Message: {Message}",
                    GetType().Name, response.Compatible, response.Message);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{class} Version handshake failed: {Error}", GetType().Name, ex.Message);
                return null;
            }
        }

        public void Dispose()
        {
            DisposeAsync().Wait();
        }
        public Task DisposeAsync()
        {
            _logger.LogTrace("{class} DisposeAsync.", GetType().Name);
            _serverEventsDisposables.Dispose();
            _hubConnectionDisposable.Dispose();

            return _connectionManager.DisposeAsync();
        }
    }
}
