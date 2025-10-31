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
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class McpServerHub : BaseHub<IClientMcpManager>, IServerMcpManager
    {
        readonly Common.Version _version;
        readonly HubEventToolsChange _eventAppToolsChange;
        readonly HubEventPromptsChange _eventAppPromptsChange;
        readonly HubEventResourcesChange _eventAppResourcesChange;
        readonly IRequestTrackingService _requestTrackingService;

        public McpServerHub(
            ILogger<McpServerHub> logger,
            Common.Version version,
            HubEventToolsChange eventAppToolsChange,
            HubEventPromptsChange eventAppPromptsChange,
            HubEventResourcesChange eventAppResourcesChange,
            IRequestTrackingService requestTrackingService)
            : base(logger)
        {
            _version = version ?? throw new ArgumentNullException(nameof(version));
            _eventAppToolsChange = eventAppToolsChange ?? throw new ArgumentNullException(nameof(eventAppToolsChange));
            _eventAppPromptsChange = eventAppPromptsChange ?? throw new ArgumentNullException(nameof(eventAppPromptsChange));
            _eventAppResourcesChange = eventAppResourcesChange ?? throw new ArgumentNullException(nameof(eventAppResourcesChange));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
        }

        public Task<ResponseData> NotifyAboutUpdatedTools(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerMcpManager.NotifyAboutUpdatedTools), _guid, data);

            _eventAppToolsChange.OnNext(new HubEventToolsChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Data = data
            });
            return ResponseData.Success(data, string.Empty).TaskFromResult();
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerMcpManager.NotifyAboutUpdatedPrompts), _guid, data);

            _eventAppPromptsChange.OnNext(new HubEventPromptsChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Data = data
            });

            return ResponseData.Success(data, string.Empty).TaskFromResult();
        }

        public Task<ResponseData> NotifyAboutUpdatedResources(string data, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerMcpManager.NotifyAboutUpdatedResources), _guid, data);

            _eventAppResourcesChange.OnNext(new HubEventResourcesChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Data = data
            });

            return ResponseData.Success(data, string.Empty).TaskFromResult();
        }

        public Task<ResponseData> NotifyToolRequestCompleted(ToolRequestCompletedData data, CancellationToken cancellationToken = default)
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

            return ResponseData.Success(string.Empty, string.Empty).TaskFromResult();
        }

        public Task<VersionHandshakeResponse> PerformVersionHandshake(VersionHandshakeRequest request, CancellationToken cancellationToken = default)
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

        private static bool IsApiVersionCompatible(string pluginApiVersion, string serverApiVersion)
        {
            if (string.IsNullOrEmpty(pluginApiVersion) || string.IsNullOrEmpty(serverApiVersion))
                return false;

            // For now, require exact version match. In the future, this could be enhanced
            // to support semantic versioning compatibility rules
            return pluginApiVersion.Equals(serverApiVersion, StringComparison.OrdinalIgnoreCase);
        }
    }
}
