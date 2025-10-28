/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/Unity-MCP)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class RemoteResourceRunner : IResourceClientHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IRequestTrackingService _requestTrackingService;
        readonly CancellationTokenSource cts = new();
        readonly CompositeDisposable _disposables = new();

        public RemoteResourceRunner(ILogger<RemoteResourceRunner> logger, IHubContext<McpServerHub> remoteAppContext, IRequestTrackingService requestTrackingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
        }

        public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent requestData, CancellationToken cancellationToken = default)
        {
            return ClientUtils.InvokeAsync<RequestResourceContent, ResponseResourceContent[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunResourceContent,
                request: requestData,
                cancellationToken: cancellationToken);
        }

        public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources requestData, CancellationToken cancellationToken = default)
        {
            return ClientUtils.InvokeAsync<RequestListResources, ResponseListResource[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunListResources,
                request: requestData,
                cancellationToken: cancellationToken);
        }

        public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates requestData, CancellationToken cancellationToken = default)
        {
            return ClientUtils.InvokeAsync<RequestListResourceTemplates, ResponseResourceTemplate[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunListResourceTemplates,
                request: requestData,
                cancellationToken: cancellationToken);
        }

        public void Dispose()
        {
            _logger.LogTrace("Dispose.");
            _disposables.Dispose();

            if (!cts.IsCancellationRequested)
                cts.Cancel();

            cts.Dispose();
        }
    }
}
