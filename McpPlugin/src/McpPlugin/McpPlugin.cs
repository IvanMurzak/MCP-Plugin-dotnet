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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Skills;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPlugin : IMcpPlugin, IDisposable
    {
        private readonly ILogger<McpPlugin> _logger;
        private readonly IMcpManagerHub _mcpManagerHub;
        private readonly CompositeDisposable _disposables = new();
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ThreadSafeBool _isDisposed = new(false);
        private readonly Common.Version _version;
        private readonly ISkillFileGenerator _skillFileGenerator;
        private readonly SkillContentCollection _skillContentCollection;
        private readonly ConnectionConfig _connectionConfig;

        public ILogger Logger => _logger;
        public IMcpManager McpManager { get; private set; }
        public IMcpManagerHub McpManagerHub => _mcpManagerHub;
        public Common.Version Version => _version;
        public VersionHandshakeResponse? VersionHandshakeStatus => _mcpManagerHub?.VersionHandshakeStatus;
        public ulong ToolCallsCount => McpManager.ToolManager?.ToolCallsCount ?? 0;
        public ReadOnlyReactiveProperty<HubConnectionState> ConnectionState => _mcpManagerHub?.ConnectionState
            ?? new ReactiveProperty<HubConnectionState>(HubConnectionState.Disconnected);
        public ReadOnlyReactiveProperty<bool> KeepConnected => _mcpManagerHub?.KeepConnected
            ?? new ReactiveProperty<bool>(false);

        public McpPlugin(
            ILogger<McpPlugin> logger,
            IMcpManager mcpManager,
            IMcpManagerHub mcpManagerHub,
            Common.Version version,
            ISkillFileGenerator skillFileGenerator,
            SkillContentCollection skillContentCollection,
            IOptions<ConnectionConfig>? connectionConfig = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("{class} Ctor.", nameof(McpPlugin));

            McpManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();

            _mcpManagerHub = mcpManagerHub ?? throw new ArgumentNullException(nameof(mcpManagerHub));
            _version = version ?? throw new ArgumentNullException(nameof(version));
            _connectionConfig = connectionConfig?.Value ?? new ConnectionConfig();
            _skillFileGenerator = skillFileGenerator ?? throw new ArgumentNullException(nameof(skillFileGenerator));
            _skillContentCollection = skillContentCollection ?? throw new ArgumentNullException(nameof(skillContentCollection));
            _mcpManagerHub.ConnectionState
                .Where(state => state == HubConnectionState.Connected)
                .Where(state => !_cancellationTokenSource.Token.IsCancellationRequested)
                .Subscribe(async state =>
                {
                    _logger.LogDebug("{method}, connection state: {state}",
                        nameof(ConnectionState), state);

                    var tasks = Enumerable.Empty<Task>();

                    await _mcpManagerHub.NotifyAboutUpdatedTools(new Common.Model.RequestToolsUpdated());

                    _logger.LogDebug("{method}, initial notifications sent.",
                        nameof(ConnectionState));
                })
                .AddTo(_disposables);

            McpManager.OnForceDisconnect
                .Subscribe(_ =>
                {
                    _logger.LogDebug("{method}, force disconnect requested.",
                        nameof(McpManager.OnForceDisconnect));

                    _mcpManagerHub.Disconnect();
                })
                .AddTo(_disposables);

            McpManager.ToolManager?.OnToolsUpdated
                .ThrottleFirst(TimeSpan.FromMilliseconds(100))
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, tools updated event received.",
                        nameof(McpManager.ToolManager.OnToolsUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    GenerateSkillFilesIfNeeded();

                    if (_mcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated tools.",
                            nameof(McpManager.ToolManager.OnToolsUpdated));
                        return;
                    }

                    await _mcpManagerHub.NotifyAboutUpdatedTools(new Common.Model.RequestToolsUpdated());
                })
                .AddTo(_disposables);

            // Generate skill files for the initial set of tools on build
            GenerateSkillFilesIfNeeded();

            McpManager.PromptManager?.OnPromptsUpdated
                .ThrottleFirst(TimeSpan.FromMilliseconds(100))
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, prompts updated event received.",
                        nameof(McpManager.PromptManager.OnPromptsUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    if (_mcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated prompts.",
                            nameof(McpManager.PromptManager.OnPromptsUpdated));
                        return;
                    }

                    await _mcpManagerHub.NotifyAboutUpdatedPrompts(new Common.Model.RequestPromptsUpdated());
                })
                .AddTo(_disposables);

            McpManager.ResourceManager?.OnResourcesUpdated
                .ThrottleFirst(TimeSpan.FromMilliseconds(100))
                .Subscribe(async _ =>
                {
                    _logger.LogDebug("{method}, resources updated event received.",
                        nameof(McpManager.ResourceManager.OnResourcesUpdated));

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    if (_mcpManagerHub == null)
                    {
                        _logger.LogWarning("{method}, RPC Router is not initialized, cannot notify about updated resources.",
                            nameof(McpManager.ResourceManager.OnResourcesUpdated));
                        return;
                    }

                    await _mcpManagerHub.NotifyAboutUpdatedResources(new Common.Model.RequestResourcesUpdated());
                })
                .AddTo(_disposables);
        }

        public bool GenerateSkillFilesIfNeeded(string? path = null)
        {
            if (!_connectionConfig.GenerateSkillFiles)
                return false;

            return GenerateSkillFiles(path);
        }

        public bool GenerateSkillFiles(string? path = null)
        {
            var skillsPath = ResolveSkillsPath(path);
            var success = true;

            var tools = McpManager.ToolManager?.GetAllTools();
            if (tools != null)
            {
                var systemTools = McpManager.SystemToolManager?.GetAllTools();
                var allTools = systemTools != null ? tools.Concat(systemTools) : tools;
                if (!_skillFileGenerator.Generate(allTools, skillsPath, _connectionConfig.Host))
                    success = false;
            }

            if (_skillContentCollection.Count > 0)
            {
                if (!_skillFileGenerator.Generate(_skillContentCollection.Values, skillsPath))
                    success = false;
            }

            return success;
        }

        public bool DeleteSkillFiles(string? path = null)
        {
            var skillsPath = ResolveSkillsPath(path);
            var success = true;

            var tools = McpManager.ToolManager?.GetAllTools();
            if (tools != null)
            {
                var systemTools = McpManager.SystemToolManager?.GetAllTools();
                var allTools = systemTools != null ? tools.Concat(systemTools) : tools;
                if (!_skillFileGenerator.Delete(allTools, skillsPath))
                    success = false;
            }

            if (_skillContentCollection.Count > 0)
            {
                if (!_skillFileGenerator.Delete(_skillContentCollection.Values, skillsPath))
                    success = false;
            }

            return success;
        }

        private string ResolveSkillsPath(string? basePath)
        {
            var skillsPath = _connectionConfig.SkillsPath;

            if (Path.IsPathRooted(skillsPath))
                return Path.GetFullPath(skillsPath);

            var resolvedBase = basePath ?? Environment.CurrentDirectory;
            return Path.GetFullPath(Path.Combine(resolvedBase, skillsPath));
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
            if (_mcpManagerHub == null)
                return Task.FromResult(false);
            return _mcpManagerHub.Connect(cancellationToken);
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
            if (_mcpManagerHub == null)
                return Task.CompletedTask;
            return _mcpManagerHub.Disconnect(cancellationToken);
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
            _mcpManagerHub?.DisconnectImmediate();
        }

        public void Dispose()
        {
            if (!_isDisposed.TrySetTrue())
                return; // already disposed

            _logger.LogDebug("{method} called.", nameof(Dispose));

            _disposables.Dispose();

            try
            {
                _mcpManagerHub?.DisconnectImmediate();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {message}\n{stackTrace}", ex.Message, ex.StackTrace);
            }

            try
            {
                _mcpManagerHub?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during async disposal: {message}\n{stackTrace}", ex.Message, ex.StackTrace);
            }

            McpManager.Dispose();

            _logger.LogDebug("{method} completed.", nameof(Dispose));
        }

        ~McpPlugin() => Dispose();
    }
}
