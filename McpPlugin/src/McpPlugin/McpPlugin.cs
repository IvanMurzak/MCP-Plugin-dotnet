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
using com.IvanMurzak.McpPlugin.Common.Hub.Server;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Common
{
    public partial class McpPlugin : IMcpPlugin
    {
        readonly ILogger<McpPlugin> _logger;
        readonly IRemoteToolServerHub? _remoteToolServerHub;
        readonly IRemotePromptServerHub? _remotePromptServerHub;
        readonly IRemoteResourceServerHub? _remoteResourceServerHub;
        readonly CompositeDisposable _disposables = new();

        public ILogger Logger => _logger;
        public IMcpManager McpRunner { get; private set; }
        public IRemoteToolServerHub? RemoteToolServerHub => _remoteToolServerHub;
        public IRemotePromptServerHub? RemotePromptServerHub => _remotePromptServerHub;
        public IRemoteResourceServerHub? RemoteResourceServerHub => _remoteResourceServerHub;
        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _remoteToolServerHub?.ConnectionState
            ?? new ReactiveProperty<HubConnectionState>(HubConnectionState.Disconnected);
        public ReadOnlyReactiveProperty<bool> KeepConnected => _remoteToolServerHub?.KeepConnected
            ?? new ReactiveProperty<bool>(false);

        public McpPlugin(
            ILogger<McpPlugin> logger,
            IMcpManager mcpManager,
            IRemoteToolServerHub? remoteServerHub = null,
            IRemotePromptServerHub? remotePromptServerHub = null,
            IRemoteResourceServerHub? remoteResourceServerHub = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{0} Ctor.", typeof(McpPlugin).Name);

            McpRunner = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));

            var cancellationToken = _disposables.ToCancellationToken();

            _remoteToolServerHub = remoteServerHub;
            _remotePromptServerHub = remotePromptServerHub;
            _remoteResourceServerHub = remoteResourceServerHub;

            _remoteToolServerHub?.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !cancellationToken.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{class}.{method}, connection state: {2}",
                        nameof(McpPlugin),
                        nameof(ConnectionState),
                        state);

                    // Perform version handshake first
                    var handshakeResponse = await _remoteToolServerHub.PerformVersionHandshake(cancellationToken);
                    if (handshakeResponse != null && !handshakeResponse.Compatible)
                    {
                        LogVersionMismatchError(handshakeResponse);
                        // Still proceed with tool notification for now, but user will see the error
                    }

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var tasks = Enumerable.Empty<Task>();

                    await _remoteToolServerHub.NotifyAboutUpdatedTools(cancellationToken);

                    _logger.LogDebug("{class}.{method}, initial notifications sent.",
                        nameof(McpPlugin),
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            _remotePromptServerHub?.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !cancellationToken.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{class}.{method}, connection state: {2}",
                        nameof(McpPlugin),
                        nameof(ConnectionState),
                        state);

                    // Perform version handshake first
                    var handshakeResponse = await _remotePromptServerHub.PerformVersionHandshake(cancellationToken);
                    if (handshakeResponse != null && !handshakeResponse.Compatible)
                    {
                        LogVersionMismatchError(handshakeResponse);
                        // Still proceed with tool notification for now, but user will see the error
                    }

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await _remotePromptServerHub.NotifyAboutUpdatedPrompts(cancellationToken);

                    _logger.LogDebug("{class}.{method}, initial notifications sent.",
                        nameof(McpPlugin),
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            _remoteResourceServerHub?.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !cancellationToken.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{class}.{method}, connection state: {2}",
                        nameof(McpPlugin),
                        nameof(ConnectionState),
                        state);

                    await _remoteResourceServerHub.NotifyAboutUpdatedResources(cancellationToken);

                    _logger.LogDebug("{class}.{method}, initial notifications sent.",
                        nameof(McpPlugin),
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            McpRunner.OnToolsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, tools updated event received.",
                        nameof(McpPlugin),
                        nameof(McpRunner.OnToolsUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remoteToolServerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated tools.",
                            nameof(McpPlugin),
                            nameof(McpRunner.OnToolsUpdated));
                        return;
                    }

                    await _remoteToolServerHub.NotifyAboutUpdatedTools(cancellationToken);
                })
                .AddTo(_disposables);

            McpRunner.OnPromptsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, prompts updated event received.",
                        nameof(McpPlugin),
                        nameof(McpRunner.OnPromptsUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remotePromptServerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated prompts.",
                            nameof(McpPlugin),
                            nameof(McpRunner.OnPromptsUpdated));
                        return;
                    }

                    await _remotePromptServerHub.NotifyAboutUpdatedPrompts(cancellationToken);
                })
                .AddTo(_disposables);

            McpRunner.OnResourcesUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, resources updated event received.",
                        nameof(McpPlugin),
                        nameof(McpRunner.OnResourcesUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remoteResourceServerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated resources.",
                            nameof(McpPlugin),
                            nameof(McpRunner.OnResourcesUpdated));
                        return;
                    }

                    await _remoteResourceServerHub.NotifyAboutUpdatedResources(cancellationToken);
                })
                .AddTo(_disposables);

            if (HasInstance)
            {
                _logger.LogError($"{nameof(McpPlugin)} already created. Use Singleton instance.");
                return;
            }

            _instance.Value = this;

            // Dispose if another instance is created, because only one instance is allowed.
            _instance
                .Where(instance => instance != this)
                .Subscribe(instance => Dispose())
                .AddTo(_disposables);
        }

        public Task<bool> Connect(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("{class}.{method} called.", nameof(McpPlugin), nameof(Connect));
            if (_remoteToolServerHub == null)
                return Task.FromResult(false);
            return _remoteToolServerHub.Connect(cancellationToken);
        }

        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("{class}.{method} called.", nameof(McpPlugin), nameof(Disconnect));
            if (_remoteToolServerHub == null)
                return Task.CompletedTask;
            return _remoteToolServerHub.Disconnect(cancellationToken);
        }

        public void Dispose()
        {
            _logger.LogInformation("{class}.{method} called.", nameof(McpPlugin), nameof(Dispose));
#pragma warning disable CS4014
            DisposeAsync();
            // DisposeAsync().Wait();
            // Unity won't reload Domain if we call DisposeAsync().Wait() here.
#pragma warning restore CS4014
        }

        public async Task DisposeAsync()
        {
            _logger.LogInformation("{class}.{method} called.", nameof(McpPlugin), nameof(DisposeAsync));

            _disposables.Dispose();

            var localInstance = _instance.CurrentValue;
            if (localInstance == this)
                _instance.Value = null;

            try
            {
                if (_remoteToolServerHub != null)
                    await _remoteToolServerHub.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {0}\n{1}", ex.Message, ex.StackTrace);
            }

            try
            {
                if (_remoteToolServerHub != null)
                    await _remoteToolServerHub.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {0}\n{1}", ex.Message, ex.StackTrace);
            }
        }

        ~McpPlugin() => Dispose();
    }
}
