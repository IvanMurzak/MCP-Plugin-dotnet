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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public class McpManager : IMcpManager, IClientMcpManager
    {
        protected readonly ILogger _logger;
        protected readonly Reflector _reflector;
        private readonly Subject<Unit> _onForceDisconnect = new();
        private readonly Subject<McpClientData> _onClientConnected = new();

        readonly IToolManager? _tools;
        readonly IPromptManager? _prompts;
        readonly IResourceManager? _resources;

        public Reflector Reflector => _reflector;
        public IToolManager? ToolManager => _tools;
        public IPromptManager? PromptManager => _prompts;
        public IResourceManager? ResourceManager => _resources;

        public IClientToolHub? ToolHub => _tools;
        public IClientPromptHub? PromptHub => _prompts;
        public IClientResourceHub? ResourceHub => _resources;

        public Observable<Unit> OnForceDisconnect => _onForceDisconnect.AsObservable();
        public Observable<McpClientData> OnClientConnected => _onClientConnected.AsObservable();

        public McpManager(
            ILogger<McpManager> logger,
            Reflector reflector,
            IToolManager? tools = null,
            IPromptManager? prompts = null,
            IResourceManager? resources = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor");

            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));

            _tools = tools;
            _prompts = prompts;
            _resources = resources;
        }

        public Task OnMcpClientConnected(McpClientData clientData)
        {
            _onClientConnected.OnNext(clientData);
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

        public Task ForceDisconnect()
        {
            _onForceDisconnect.OnNext(Unit.Default);
            return Task.CompletedTask;
        }
    }
}
