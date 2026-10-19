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
using System.Collections.Generic;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpManager : IMcpManager, IClientMcpManager
    {
        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;
        // Volatile ensures the reference write is visible to all threads without CPU/JIT caching.
        // Thread-safety contract: arrays are never mutated after assignment — only replaced atomically.
        // Readers always observe either the previous or next snapshot, never a torn state.
        private volatile IReadOnlyList<McpClientData> _activeClients = Array.Empty<McpClientData>();
        private readonly Subject<Unit> _onForceDisconnect = new();
        private readonly Subject<McpClientData> _onClientConnected = new();
        private readonly Subject<McpClientData> _onClientDisconnected = new();
        private readonly Subject<IReadOnlyList<McpClientData>> _onClientsChanged = new();

        readonly IToolManager? _tools;
        readonly IPromptManager? _prompts;
        readonly IResourceManager? _resources;
        readonly ISystemToolManager? _systemTools;

        public Reflector Reflector => _reflector;
        public IToolManager? ToolManager => _tools;
        public IPromptManager? PromptManager => _prompts;
        public IResourceManager? ResourceManager => _resources;
        public ISystemToolManager? SystemToolManager => _systemTools;

        readonly McpRequestActivity _requestActivity = new();
        readonly IClientToolHub? _toolHub;
        readonly IClientPromptHub? _promptHub;
        readonly IClientResourceHub? _resourceHub;
        readonly IClientSystemToolHub? _systemToolHub;

        /// <inheritdoc cref="IMcpManager.RequestActivity"/>
        public McpRequestActivity RequestActivity => _requestActivity;

        // The hubs are what the MCP server calls; they wrap the managers to count each request in RequestActivity.
        public IClientToolHub? ToolHub => _toolHub;
        public IClientPromptHub? PromptHub => _promptHub;
        public IClientResourceHub? ResourceHub => _resourceHub;
        public IClientSystemToolHub? SystemToolHub => _systemToolHub;

        public IReadOnlyList<McpClientData> ActiveClients => _activeClients;
        public Observable<Unit> OnForceDisconnect => _onForceDisconnect.AsObservable();
        public Observable<McpClientData> OnClientConnected => _onClientConnected.AsObservable();
        public Observable<McpClientData> OnClientDisconnected => _onClientDisconnected.AsObservable();
        public Observable<IReadOnlyList<McpClientData>> OnClientsChanged => _onClientsChanged.AsObservable();

        public McpManager(
            ILogger<McpManager> logger,
            Reflector reflector,
            IToolManager? tools = null,
            IPromptManager? prompts = null,
            IResourceManager? resources = null,
            ISystemToolManager? systemTools = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");

            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));

            _tools = tools;
            _prompts = prompts;
            _resources = resources;
            _systemTools = systemTools;

            _toolHub = tools == null ? null : new TrackedToolHub(tools, _requestActivity);
            _promptHub = prompts == null ? null : new TrackedPromptHub(prompts, _requestActivity);
            _resourceHub = resources == null ? null : new TrackedResourceHub(resources, _requestActivity);
            _systemToolHub = systemTools == null ? null : new TrackedSystemToolHub(systemTools, _requestActivity);
        }

        public Task OnMcpClientConnected(McpClientData connectedClient, McpClientData[] allActiveClients)
        {
            _activeClients = allActiveClients;
            _onClientConnected.OnNext(connectedClient);
            _onClientsChanged.OnNext(allActiveClients);
            return Task.CompletedTask;
        }

        public Task OnMcpClientDisconnected(McpClientData disconnectedClient, McpClientData[] remainingClients)
        {
            _activeClients = remainingClients;
            _onClientDisconnected.OnNext(disconnectedClient);
            _onClientsChanged.OnNext(remainingClients);
            return Task.CompletedTask;
        }

        public Task OnInitialClientData(McpClientData[] allActiveClients)
        {
            _activeClients = allActiveClients;
            _onClientsChanged.OnNext(allActiveClients);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _logger.LogDebug("{method} called.", nameof(Dispose));

            _tools?.Dispose();
            _prompts?.Dispose();
            _resources?.Dispose();

            _logger.LogDebug("{method} completed.", nameof(Dispose));
        }

        public Task ForceDisconnect(string? reason = null)
        {
            _onForceDisconnect.OnNext(Unit.Default);
            return Task.CompletedTask;
        }
    }
}
