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
using com.IvanMurzak.McpPlugin.Server.Tools;
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
            if (_connectionRejected)
                return;

            // A reconnecting plugin takes its remembered lists back (or a replaced instance's are dropped).
            // A plugin whose role is decided at its handshake does this in PerformVersionHandshake.
            var catalog = Catalog;
            if (_strategy is IPluginRoleStrategy roles && roles.Registry.GetRole(Context.ConnectionId) == ClientRole.Editor)
                OnEditorRouted(roles, catalog);
            else
                catalog?.OnPluginConnected(Context.ConnectionId);

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
                // Plugin-plane validation (auth-fixes B11): the plugin hub-token carries
                // aud=urn:agd:hub, which the strict agent-plane aud check rejects. The plugin plane
                // allow-lists that audience while keeping the agent plane strict on the RS resource, so
                // an unregistered-bucket → silent "3 tools" degrade no longer happens. Plane separation
                // is enforced in the validator: an agent token can never register as a plugin instance.
                validation = await _oauthValidator.ValidateAsync(token!, TokenValidationPlane.Plugin, Context.ConnectionAborted);
            }
            catch (Exception ex)
            {
                // Any failed validation attempt is unavailable evidence, not a definitive denial.
                // Close the unregistered hub so it cannot remain silently unroutable after recovery.
                _logger.LogWarning(ex, "{guid} oauth plugin validation unavailable; closing for retry. ConnectionId: {connectionId}.", _guid, Context.ConnectionId);
                _strategy.OnPluginDisconnected(GetType(), Context.ConnectionId, _logger);
                _connectionRejected = true;
                Context.Abort();
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
                machineName: QueryValue(query, Consts.MCP.Server.HubQuery.MachineName),
                projectPathHashLegacy: QueryValue(query, Consts.MCP.Server.HubQuery.ProjectPathHashLegacy));

            account.RegisterInstance(identity, metadata, Context.ConnectionId, _logger);
        }

        /// <summary>
        /// The host's <see cref="PluginCatalogCache"/>, read from the connection's request services so the
        /// public constructor stays unchanged. Null outside a real hub connection. Not cached: SignalR
        /// creates a hub instance per invocation, so each instance reads it about once.
        /// </summary>
        PluginCatalogCache? Catalog
            => Context?.GetHttpContext()?.RequestServices?.GetService(typeof(PluginCatalogCache)) as PluginCatalogCache;

        /// <summary>
        /// This connection is now the editor agents are routed to: its remembered lists come back. When a
        /// game stood in for the editor, agents may have listed the game's tools, so the editor's next tool
        /// change is forwarded even if it looks unchanged.
        /// </summary>
        void OnEditorRouted(IPluginRoleStrategy roles, PluginCatalogCache? catalog)
        {
            if (roles.TakeRuntimeServed())
                catalog?.NoteToolListMissed();
            catalog?.OnPluginConnected(Context.ConnectionId);
        }

        /// <summary>A compatible handshake: a plugin that did not declare its role gets one from its environment now.</summary>
        void ClassifyPlugin(string? environment)
        {
            if (_strategy is not IPluginRoleStrategy roles)
                return;
            try
            {
                var clients = Clients;
                if (roles.OnPluginHandshake(GetType(), Context.ConnectionId, environment, _logger,
                        (id, reason) => _ = clients.Client(id).ForceDisconnect(reason)))
                    OnEditorRouted(roles, Catalog);
            }
            catch (Exception ex)
            {
                // Never fail a handshake over role bookkeeping.
                _logger.LogWarning(ex, "{guid} Could not classify plugin connection '{connectionId}'.", _guid, Context.ConnectionId);
            }
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

            PluginArgumentPolicy.Forget(Context.ConnectionId);
            _webhookCollector.OnPluginDisconnected(Context.ConnectionId);
            // Every plugin-side close keeps the lists for the reconnect grace: the server cannot tell a
            // recompile from a closed project. See PluginCatalogCache for what drops them early.
            Catalog?.OnPluginDisconnected(Context.ConnectionId);
            var wasRuntime = _strategy is IPluginRoleStrategy roles && roles.Registry.GetRole(Context.ConnectionId) == ClientRole.Runtime;
            var disconnected = base.OnDisconnectedAsync(exception);
            // The game's own tools were part of tools/list (see RemoteToolRunner): agents list again without them.
            if (wasRuntime)
                _eventAppToolsChange.OnNext(new HubEventToolsChange.EventData { ConnectionId = Context.ConnectionId, Request = new RequestToolsUpdated() });
            return disconnected;
        }

        public Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request) => NotifyAboutUpdatedTools(request, default);
        protected virtual Task<ResponseData> NotifyAboutUpdatedTools(RequestToolsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerToolHub.NotifyAboutUpdatedTools), _guid, request);

            var eventData = new HubEventToolsChange.EventData
            {
                ConnectionId = Context.ConnectionId,
                Request = request
            };
            // The plugin sends this on every (re)connect. When its previous tools are known, agents
            // are told only if the set really changed.
            var catalog = Catalog;
            if (catalog == null || !catalog.TryNotifyIfToolsChanged(Context.ConnectionId, () => _eventAppToolsChange.OnNext(eventData)))
                _eventAppToolsChange.OnNext(eventData);

            return ResponseData.Success(request.RequestId, "Received tools update notification").TaskFromResult();
        }

        public Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request) => NotifyAboutUpdatedPrompts(request, default);
        protected virtual Task<ResponseData> NotifyAboutUpdatedPrompts(RequestPromptsUpdated request, CancellationToken cancellationToken = default)
        {
            _logger.LogTrace("{method}. {guid}. Data: {data}",
                nameof(IServerPromptHub.NotifyAboutUpdatedPrompts), _guid, request);

            Catalog?.Invalidate(Context.ConnectionId, PluginCatalogKind.Prompts);

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

            var catalog = Catalog;
            catalog?.Invalidate(Context.ConnectionId, PluginCatalogKind.Resources);
            catalog?.Invalidate(Context.ConnectionId, PluginCatalogKind.ResourceTemplates);
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

                    // Classified only after an accepted handshake: a refused plugin must not replace a working one.
                    ClassifyPlugin(request.Environment);
                    // Which argument checks this plugin gets (McpPlugin 8.14.0+ reports its version).
                    PluginArgumentPolicy.Record(Context.ConnectionId, request.McpPluginVersion);

                    var pluginToken = ClientUtils.GetTokenByConnectionId(Context.ConnectionId)
                        ?? (_strategy as IPluginRoleStrategy)?.Registry.GetToken(Context.ConnectionId);
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
