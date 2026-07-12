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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.SignalR;
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
        readonly IWebhookEventCollector _webhookCollector;
        readonly IOAuthTokenValidator? _oauthValidator;

        public McpServerHub(
            ILogger<McpServerHub> logger,
            Common.Version version,
            IDataArguments dataArguments,
            HubEventToolsChange eventAppToolsChange,
            HubEventPromptsChange eventAppPromptsChange,
            HubEventResourcesChange eventAppResourcesChange,
            IRequestTrackingService requestTrackingService,
            IMcpSessionTracker sessionTracker,
            IMcpConnectionStrategy strategy,
            IWebhookEventCollector webhookCollector,
            IAuthorizationWebhookService authorizationWebhookService,
            IOAuthTokenValidator? oauthValidator = null)
            : base(logger, strategy, authorizationWebhookService)
        {
            _version = version ?? throw new ArgumentNullException(nameof(version));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _eventAppToolsChange = eventAppToolsChange ?? throw new ArgumentNullException(nameof(eventAppToolsChange));
            _eventAppPromptsChange = eventAppPromptsChange ?? throw new ArgumentNullException(nameof(eventAppPromptsChange));
            _eventAppResourcesChange = eventAppResourcesChange ?? throw new ArgumentNullException(nameof(eventAppResourcesChange));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
            _webhookCollector = webhookCollector ?? throw new ArgumentNullException(nameof(webhookCollector));
            _oauthValidator = oauthValidator;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            if (_connectionRejected)
                return;

            // mcp-authorize b3: in oauth mode a plugin connection is validated (its `sub` is the
            // account) and its instance metadata registered into the account+instance pairing plane
            // BEFORE we compute the account-scoped initial client data below.
            await TryRegisterOAuthInstanceAsync();

            var allActiveClients = _strategy.GetAllClientData(Context.ConnectionId, _sessionTracker);
            _logger.LogDebug("{method}. {guid}. Sending initial client data. Count: {count}",
                nameof(OnConnectedAsync), _guid, allActiveClients.Length);
            await Clients.Caller.OnInitialClientData(allActiveClients);
        }

        /// <summary>
        /// Registers this plugin connection into the <see cref="AccountMcpStrategy"/> registry when in
        /// oauth mode (mcp-authorize b3). The account is taken from the VALIDATED plugin token (its
        /// <c>sub</c>) — never from unauthenticated input — so an instance can never be registered
        /// under a spoofed account. Instance metadata is read from the hub-connection query (the b7
        /// wire format); when absent, a synthetic single-instance registration keeps the common case
        /// routable. A token that fails validation is left unregistered (unroutable), not trusted.
        /// </summary>
        async Task TryRegisterOAuthInstanceAsync()
        {
            if (_strategy.AuthOption != Consts.MCP.Server.AuthOption.oauth)
                return;
            if (_strategy is not AccountMcpStrategy account)
                return;

            var httpContext = Context.GetHttpContext();
            var token = ExtractBearer(httpContext?.Request.Headers["Authorization"].FirstOrDefault());
            if (string.IsNullOrEmpty(token) || _oauthValidator == null)
            {
                _logger.LogWarning("{guid} oauth plugin connection without a validatable token — not registered (unroutable). ConnectionId: {connectionId}.",
                    _guid, Context.ConnectionId);
                return;
            }

            OAuthValidationResult validation;
            try
            {
                validation = await _oauthValidator.ValidateAsync(token!, Context.ConnectionAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{guid} oauth plugin token validation threw — not registered. ConnectionId: {connectionId}.", _guid, Context.ConnectionId);
                return;
            }

            var identity = validation.Succeeded ? ConnectionIdentity.Create(validation.Subject, validation.Scope, validation.ClientId) : null;
            if (identity == null)
            {
                _logger.LogWarning("{guid} oauth plugin token rejected ({reason}) — not registered (unroutable). ConnectionId: {connectionId}.",
                    _guid, validation.FailureReason ?? "no subject", Context.ConnectionId);
                return;
            }

            var query = httpContext?.Request.Query;
            var metadata = AccountMcpStrategy.BuildInstanceMetadata(
                connectionId: Context.ConnectionId,
                instanceId: QueryValue(query, Consts.MCP.Server.HubQuery.InstanceId),
                engine: QueryValue(query, Consts.MCP.Server.HubQuery.Engine),
                projectName: QueryValue(query, Consts.MCP.Server.HubQuery.ProjectName),
                projectPathHash: QueryValue(query, Consts.MCP.Server.HubQuery.ProjectPathHash),
                machineName: QueryValue(query, Consts.MCP.Server.HubQuery.MachineName));

            account.RegisterInstance(identity, metadata, Context.ConnectionId, _logger);
        }

        static string? QueryValue(Microsoft.AspNetCore.Http.IQueryCollection? query, string key)
        {
            if (query == null || !query.TryGetValue(key, out var value))
                return null;
            var s = value.ToString();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        static string? ExtractBearer(string? authHeader)
        {
            if (string.IsNullOrEmpty(authHeader) || !authHeader!.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;
            var token = authHeader.Substring("Bearer ".Length).Trim();
            return token.Length > 0 ? token : null;
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionRejected)
                return Task.CompletedTask;

            _webhookCollector.OnPluginDisconnected(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
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

                    var pluginToken = ClientUtils.GetTokenByConnectionId(Context.ConnectionId);
                    _webhookCollector.OnPluginConnected(Context.ConnectionId, pluginToken, request.Environment, request.PluginVersion);
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

        public Task<McpClientData[]> GetMcpClientData()
        {
            try
            {
                _logger.LogTrace("{method}. {guid}.",
                    nameof(IServerMcpManager.GetMcpClientData), _guid);

                var clientData = _strategy.GetAllClientData(Context.ConnectionId, _sessionTracker);

                _logger.LogDebug("{method}. {guid}. ClientData count: {count}",
                    nameof(IServerMcpManager.GetMcpClientData), _guid, clientData.Length);

                return Task.FromResult(clientData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting MCP Client Data");
                return Task.FromResult(Array.Empty<McpClientData>());
            }
        }

        public Task<McpServerData> GetMcpServerData()
        {
            try
            {
                _logger.LogTrace("{method}. {guid}.",
                    nameof(IServerMcpManager.GetMcpServerData), _guid);

                var serverData = _strategy.GetServerData(Context.ConnectionId, _sessionTracker);

                if (serverData == null)
                    throw new Exception("Server data is null");

                _logger.LogDebug("{method}. {guid}. ServerData, isAiAgentConnected: {isAiAgentConnected}, serverVersion: {serverVersion}, serverApiVersion: {serverApiVersion}, serverTransport: {serverTransport}",
                    nameof(IServerMcpManager.GetMcpServerData), _guid, serverData.IsAiAgentConnected, serverData.ServerVersion, serverData.ServerApiVersion, serverData.ServerTransport);

                return Task.FromResult(serverData);
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
