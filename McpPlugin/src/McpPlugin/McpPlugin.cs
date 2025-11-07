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
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPlugin : IMcpPlugin, IDisposable
    {
        private readonly ILogger<McpPlugin> _logger;
        private readonly IRemoteMcpManagerHub _remoteMcpManagerHub;
        private readonly CompositeDisposable _disposables = new();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ThreadSafeBool _isDisposed = new(false);

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
            _logger.LogTrace("{class} Ctor.", nameof(McpPlugin));

            McpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();

            _remoteMcpManagerHub = remoteMcpManagerHub ?? throw new ArgumentNullException(nameof(remoteMcpManagerHub));
            _remoteMcpManagerHub.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !_cancellationTokenSource.Token.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{method}, connection state: {state}",
                        nameof(ConnectionState), state);

                    var tasks = Enumerable.Empty<Task>();

                    await _remoteMcpManagerHub.NotifyAboutUpdatedTools(new Common.Model.RequestToolsUpdated());

                    _logger.LogDebug("{method}, initial notifications sent.",
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            McpManager.OnForceDisconnect
                .Subscribe(_ =>
                {
                    _logger.LogDebug("{method}, force disconnect requested.",
                        nameof(McpManager.OnForceDisconnect));

                    _remoteMcpManagerHub.Disconnect();
                })
                .AddTo(_disposables);

            McpManager.ToolManager?.OnToolsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, tools updated event received.",
                        nameof(McpManager.ToolManager.OnToolsUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated tools.",
                            nameof(McpManager.ToolManager.OnToolsUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedTools(new Common.Model.RequestToolsUpdated());
                })
                .AddTo(_disposables);

            McpManager.PromptManager?.OnPromptsUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, prompts updated event received.",
                        nameof(McpManager.PromptManager.OnPromptsUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated prompts.",
                            nameof(McpManager.PromptManager.OnPromptsUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedPrompts(new Common.Model.RequestPromptsUpdated());
                })
                .AddTo(_disposables);

            McpManager.ResourceManager?.OnResourcesUpdated
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, resources updated event received.",
                        nameof(McpManager.ResourceManager.OnResourcesUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    if (_remoteMcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated resources.",
                            nameof(McpManager.ResourceManager.OnResourcesUpdated));
                        return;
                    }

                    await _remoteMcpManagerHub.NotifyAboutUpdatedResources(new Common.Model.RequestResourcesUpdated());
                })
                .AddTo(_disposables);

            if (HasInstance)
            {
                _logger.LogError($"Instance already created. Use Singleton instance.");
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
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called but already disposed, ignored.",
                    nameof(Connect));
                return Task.FromResult(false); // already disposed
            }
            _logger.LogDebug("{method} called.", nameof(Connect));
            if (_remoteMcpManagerHub == null)
                return Task.FromResult(false);
            return _remoteMcpManagerHub.Connect(cancellationToken);
        }

        public Task Disconnect(CancellationToken cancellationToken = default)
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called but already disposed, ignored.",
                    nameof(Disconnect));
                return Task.CompletedTask; // already disposed
            }
            _logger.LogDebug("{method} called.", nameof(Disconnect));
            if (_remoteMcpManagerHub == null)
                return Task.CompletedTask;
            return _remoteMcpManagerHub.Disconnect(cancellationToken);
        }

        public void DisconnectImmediate()
        {
            if (_isDisposed.Value)
            {
                _logger.LogWarning("{method} called but already disposed, ignored.",
                    nameof(DisconnectImmediate));
                return; // already disposed
            }
            _logger.LogDebug("{method} called.", nameof(DisconnectImmediate));
            _remoteMcpManagerHub?.DisconnectImmediate();
        }

        public void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogInformation("{method} called.", nameof(Dispose));

            _disposables.Dispose();

            var localInstance = _instance.CurrentValue;
            if (localInstance == this)
                _instance.Value = null;

            try
            {
                _remoteMcpManagerHub?.DisconnectImmediate();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {message}\n{stackTrace}", ex.Message, ex.StackTrace);
            }

            try
            {
                _remoteMcpManagerHub?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {message}\n{stackTrace}", ex.Message, ex.StackTrace);
            }

            McpManager.Dispose();

            _logger.LogInformation("{method} completed.", nameof(Dispose));
        }

        ~McpPlugin() => Dispose();
    }
}
