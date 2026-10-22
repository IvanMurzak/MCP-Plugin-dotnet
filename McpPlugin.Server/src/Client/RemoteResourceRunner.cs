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
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class RemoteResourceRunner : IClientResourceHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IDataArguments _dataArguments;
        readonly IRequestTrackingService _requestTrackingService;
        readonly IMcpConnectionStrategy _strategy;
        readonly CompositeDisposable _disposables = new();
        readonly CancellationTokenSource _cancellationTokenSource;
        readonly PluginCatalogCache? _catalog;

        public RemoteResourceRunner(ILogger<RemoteResourceRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService, IMcpConnectionStrategy strategy)
            : this(logger, remoteAppContext, dataArguments, requestTrackingService, strategy, catalog: null)
        {
        }

        /// <summary>See <see cref="RemoteToolRunner"/>'s catalog constructor; the same rules apply to resources.</summary>
        public RemoteResourceRunner(ILogger<RemoteResourceRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService, IMcpConnectionStrategy strategy, PluginCatalogCache? catalog)
        {
            _catalog = catalog;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();
        }

        public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent requestData) => RunResourceContent(requestData, default);
        public async Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent requestData, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            return await CatalogAwareInvoke.SendAsync<RequestResourceContent, ResponseResourceContent[]>(
                _logger, _remoteAppContext, nameof(IClientResourceHub.RunResourceContent), requestData, _dataArguments.PluginTimeoutMs,
                retryAfterDelivery: true, _strategy, _catalog, cancellationToken);
        }

        public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources requestData) => RunListResources(requestData, default);
        public async Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources requestData, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            return await CatalogAwareInvoke.ListAsync<RequestListResources, ResponseListResource[]>(
                _logger, _remoteAppContext, nameof(IClientResourceHub.RunListResources), requestData, _dataArguments,
                _strategy, _catalog, PluginCatalogKind.Resources, cancellationToken);
        }

        public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates requestData) => RunResourceTemplates(requestData, default);
        public async Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates requestData, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            return await CatalogAwareInvoke.ListAsync<RequestListResourceTemplates, ResponseResourceTemplate[]>(
                _logger, _remoteAppContext, nameof(IClientResourceHub.RunResourceTemplates), requestData, _dataArguments,
                _strategy, _catalog, PluginCatalogKind.ResourceTemplates, cancellationToken);
        }

        public void Dispose()
        {
            _logger.LogTrace("Dispose.");
            _disposables.Dispose();
        }
    }
}
