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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpServerHub : BaseHub<IClientMcpRpc>, IServerMcpManager
    {
        readonly Common.Version _version;
        readonly IDataArguments _dataArguments;
        readonly HubEventToolsChange _eventAppToolsChange;
        readonly HubEventPromptsChange _eventAppPromptsChange;
        readonly HubEventResourcesChange _eventAppResourcesChange;
        readonly IRequestTrackingService _requestTrackingService;
        readonly IMcpSessionTracker _sessionTracker;

        public McpServerHub(
            ILogger<McpServerHub> logger,
            Common.Version version,
            IDataArguments dataArguments,
            HubEventToolsChange eventAppToolsChange,
            HubEventPromptsChange eventAppPromptsChange,
            HubEventResourcesChange eventAppResourcesChange,
            IRequestTrackingService requestTrackingService,
            IMcpSessionTracker sessionTracker)
            : base(logger)
        {
            _version = version ?? throw new ArgumentNullException(nameof(version));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _eventAppToolsChange = eventAppToolsChange ?? throw new ArgumentNullException(nameof(eventAppToolsChange));
            _eventAppPromptsChange = eventAppPromptsChange ?? throw new ArgumentNullException(nameof(eventAppPromptsChange));
            _eventAppResourcesChange = eventAppResourcesChange ?? throw new ArgumentNullException(nameof(eventAppResourcesChange));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
        }

        public Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request) => NotifyAboutUpdatedTools(request, default);
        protected virtual Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerToolHub.NotifyAboutUpdatedTools), _guid, request);

            _eventAppToolsChange.OnNext(new HubEventToolsChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Request = request
            });
            return ResponseData.Success(request.RequestId, "Received tools update notification").TaskFromResult();
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request) => NotifyAboutUpdatedPrompts(request, default);
        protected virtual Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerPromptHub.NotifyAboutUpdatedPrompts), _guid, request);

            _eventAppPromptsChange.OnNext(new HubEventPromptsChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Request = request
            });

            return ResponseData.Success(request.RequestId, "Received prompts update notification").TaskFromResult();
        }

        public Task<ResponseData> NotifyAboutUpdatedResources(RequestResourcesUpdated request) => NotifyAboutUpdatedResources(request, default);
        protected virtual Task<ResponseData> NotifyAboutUpdatedResources(RequestResourcesUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerResourceHub.NotifyAboutUpdatedResources), _guid, request);
            _eventAppResourcesChange.OnNext(new HubEventResourcesChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Request = request
            });

            return ResponseData.Success(request.RequestId, "Received resources update notification").TaskFromResult();
        }

        public Task<ResponseData> NotifyToolRequestCompleted(RequestToolCompletedData data) => NotifyToolRequestCompleted(data, default);
        protected virtual Task<ResponseData> NotifyToolRequestCompleted(RequestToolCompletedData data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. RequestId: {requestId}",
                nameof(IServerMcpManager.NotifyToolRequestCompleted), _guid, data.RequestId);

            try
            {
                _requestTrackingService.CompleteRequest(data.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing tool response for RequestId: {requestId}", data.RequestId);
            }

            return ResponseData.Success(data.RequestId, string.Empty).TaskFromResult();
        }

        public Task<VersionHandshakeResponse> PerformVersionHandshake(RequestVersionHandshake request) => PerformVersionHandshake(request, default);
        protected virtual Task<VersionHandshakeResponse> PerformVersionHandshake(RequestVersionHandshake request, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogTrace("{method}. {guid}. PluginVersion: {pluginVersion}, ApiVersion: {apiVersion}, Environment: {environment}",
                    nameof(IServerMcpManager.PerformVersionHandshake), _guid, request.PluginVersion, request.ApiVersion, request.Environment);

                var serverApiVersion = _version.Api;
                var isApiVersionCompatible = IsApiVersionCompatible(request.ApiVersion, serverApiVersion);

                var response = new VersionHandshakeResponse
                {
                    ApiVersion = serverApiVersion,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
                    Compatible = isApiVersionCompatible,
                    Message = isApiVersionCompatible
                        ? "API version is compatible."
                        : $"API version mismatch. Plugin: {request.ApiVersion}, Server: {serverApiVersion}. Please update to compatible versions."
                };

                if (!isApiVersionCompatible)
                {
                    _logger.LogError("API version mismatch detected. Plugin: {pluginApiVersion}, Server: {serverApiVersion}",
                        request.ApiVersion, serverApiVersion);
                }
                else
                {
                    _logger.LogInformation("Version handshake successful. Plugin: {pluginVersion}, API: {apiVersion}, Environment: {environment}",
                        request.PluginVersion, request.ApiVersion, request.Environment);
                }

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during version handshake.");
                return Task.FromResult(new VersionHandshakeResponse
                {
                    ApiVersion = "Unknown",
                    ServerVersion = "Unknown",
                    Compatible = false,
                    Message = $"Error during version handshake: {ex.Message}"
                });
            }
        }

        public Task<McpClientData> GetMcpClientData()
        {
            try
            {
                return Task.FromResult(_sessionTracker.GetClientData());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting MCP Client Data");
                return Task.FromResult(new McpClientData { IsConnected = false });
            }
        }

        public Task<McpServerData> GetMcpServerData()
        {
            try
            {
                return Task.FromResult(_sessionTracker.GetServerData());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting MCP Server Data");
                return Task.FromResult(new McpServerData
                {
                    IsAiAgentConnected = false,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    ServerApiVersion = _version.Api,
                    ServerTransport = _dataArguments.ClientTransport
                });
            }
        }

        protected virtual bool IsApiVersionCompatible(string pluginApiVersion, string serverApiVersion)
        {
            if (string.IsNullOrEmpty(pluginApiVersion) || string.IsNullOrEmpty(serverApiVersion))
                return false;

            // For now, require exact version match. In the future, this could be enhanced
            // to support semantic versioning compatibility rules
            return pluginApiVersion.Equals(serverApiVersion, StringComparison.OrdinalIgnoreCase);
        }
    }
}
