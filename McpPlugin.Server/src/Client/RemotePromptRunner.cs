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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class RemotePromptRunner : IClientPromptHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IDataArguments _dataArguments;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IRequestTrackingService _requestTrackingService;
        readonly CompositeDisposable _disposables = new();
        readonly CancellationTokenSource _cancellationTokenSource;

        public RemotePromptRunner(ILogger<RemotePromptRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();
        }

        public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request) => RunGetPrompt(request, default);
        public async Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var responseData = await ClientUtils.InvokeAsync<RequestGetPrompt, ResponseGetPrompt, McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: nameof(IClientPromptHub.RunGetPrompt),
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: cancellationToken);

            if (responseData.Value != null)
                return responseData.Value.Pack(request.RequestID);

            return ResponseGetPrompt.Error("Response data is null").Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request) => RunListPrompts(request, default);
        public async Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var response = await ClientUtils.InvokeAsync<RequestListPrompts, ResponseListPrompts, McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: nameof(IClientPromptHub.RunListPrompts),
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: cancellationToken);

            if (response.Status == ResponseStatus.Error)
                return ResponseData<ResponseListPrompts>.Error(request.RequestID, response.Message ?? "Got an error during listing tools");

            return response;
        }
        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemotePromptRunner).Name);
            _disposables.Dispose();
        }
    }
}
