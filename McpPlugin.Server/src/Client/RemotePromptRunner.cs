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

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request) => RunCallTool(request, default);
        public async Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken).Token;

            var response = await _requestTrackingService.TrackRequestAsync(
                request.RequestID,
                async () =>
                {
                    var responseData = await ClientUtils.InvokeAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                        logger: _logger,
                        hubContext: _remoteAppContext,
                        methodName: Consts.RPC.Client.RunCallTool,
                        request: request,
                        dataArguments: _dataArguments,
                        cancellationToken: cancellationToken);

                    return responseData.Value ?? ResponseCallTool.Error("Response data is null");
                },
                TimeSpan.FromMinutes(5),
                cancellationToken);

            // Wrap the ResponseCallTool back into ResponseData<ResponseCallTool>
            return response.Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request) => RunListTool(request, default);
        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken).Token;
            return ClientUtils.InvokeAsync<RequestListTool, ResponseListTool[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunListTool,
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: cancellationToken)
                .ContinueWith(task =>
            {
                var response = task.Result;
                if (response.Status == ResponseStatus.Error)
                    return ResponseData<ResponseListTool[]>.Error(request.RequestID, response.Message ?? "Got an error during listing tools");

                return response;
            }, cancellationToken: cancellationToken);
        }

        public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request) => RunGetPrompt(request, default);
        public async Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request, CancellationToken cancellationToken = default)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken).Token;

            var responseData = await ClientUtils.InvokeAsync<RequestGetPrompt, ResponseGetPrompt, McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunGetPrompt,
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: cancellationToken);

            if (responseData.Value != null)
                return responseData.Value.Pack(request.RequestID);

            return ResponseGetPrompt.Error("Response data is null").Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request) => RunListPrompts(request, default);
        public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken).Token;
            return ClientUtils.InvokeAsync<RequestListPrompts, ResponseListPrompts, McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunListPrompts,
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: cancellationToken)
                .ContinueWith(task =>
            {
                var response = task.Result;
                if (response.Status == ResponseStatus.Error)
                    return ResponseData<ResponseListPrompts>.Error(request.RequestID, response.Message ?? "Got an error during listing tools");

                return response;
            }, cancellationToken: cancellationToken);
        }
        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemotePromptRunner).Name);
            _disposables.Dispose();
        }
    }
}
