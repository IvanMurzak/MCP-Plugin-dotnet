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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Common
{
    public partial class McpPlugin : IMcpPlugin
    {
        readonly ILogger<McpPlugin> _logger;
        readonly IRemoteMcpManagerHub _remoteMcpManagerHub;
        readonly CompositeDisposable _disposables = new();

        public ILogger Logger => _logger;
        public IMcpManager McpManager { get; private set; }
        public IRemoteMcpManagerHub RemoteMcpManagerHub => _remoteMcpManagerHub;
        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _remoteMcpManagerHub?.ConnectionState
            ?? new ReactiveProperty<HubConnectionState>(HubConnectionState.Disconnected);
        public ReadOnlyReactiveProperty<bool> KeepConnected => _remoteMcpManagerHub?.KeepConnected
            ?? new ReactiveProperty<bool>(false);

        public McpPlugin(
            ILogger<McpPlugin> logger,
            IMcpManager mcpManager,
            IRemoteMcpManagerHub remoteMcpManagerHub)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{0} Ctor.", typeof(McpPlugin).Name);

            McpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));

            var cancellationToken = _disposables.ToCancellationToken();

            _remoteMcpManagerHub = remoteMcpManagerHub ?? throw new ArgumentNullException(nameof(remoteMcpManagerHub));
            _remoteMcpManagerHub.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !cancellationToken.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{class}.{method}, connection state: {2}",
                        nameof(McpPlugin),
                        nameof(ConnectionState),
                        state);

                    var tasks = Enumerable.Empty<Task>();

                    await _remoteMcpManagerHub.NotifyAboutUpdatedTools(string.Empty, cancellationToken);

                    _logger.LogDebug("{class}.{method}, initial notifications sent.",
                        nameof(McpPlugin),
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            McpManager.ToolManager?.OnToolsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, tools updated event received.",
                        nameof(McpPlugin),
                        nameof(McpManager.ToolManager.OnToolsUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated tools.",
                            nameof(McpPlugin),
                            nameof(McpManager.ToolManager.OnToolsUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedTools(string.Empty, cancellationToken);
                })
                .AddTo(_disposables);

            McpManager.PromptManager?.OnPromptsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, prompts updated event received.",
                        nameof(McpPlugin),
                        nameof(McpManager.PromptManager.OnPromptsUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated prompts.",
                            nameof(McpPlugin),
                            nameof(McpManager.PromptManager.OnPromptsUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedPrompts(string.Empty, cancellationToken);
                })
                .AddTo(_disposables);

            McpManager.ResourceManager?.OnResourcesUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{class}.{method}, resources updated event received.",
                        nameof(McpPlugin),
                        nameof(McpManager.ResourceManager.OnResourcesUpdated));

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{class}.{method}, RPC Router is not initialized, cannot notify about updated resources.",
                            nameof(McpPlugin),
                            nameof(McpManager.ResourceManager.OnResourcesUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedResources(string.Empty, cancellationToken);
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
            if (_remoteMcpManagerHub == null)
                return Task.FromResult(false);
            return _remoteMcpManagerHub.Connect(cancellationToken);
        }

        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("{class}.{method} called.", nameof(McpPlugin), nameof(Disconnect));
            if (_remoteMcpManagerHub == null)
                return Task.CompletedTask;
            return _remoteMcpManagerHub.Disconnect(cancellationToken);
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
                if (_remoteMcpManagerHub != null)
                    await _remoteMcpManagerHub.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {0}\n{1}", ex.Message, ex.StackTrace);
            }

            try
            {
                if (_remoteMcpManagerHub != null)
                    await _remoteMcpManagerHub.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {0}\n{1}", ex.Message, ex.StackTrace);
            }
        }

        ~McpPlugin() => Dispose();
    }
}
