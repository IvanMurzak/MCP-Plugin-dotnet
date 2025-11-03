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
    public class RemoteToolRunner : IClientToolHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IDataArguments _dataArguments;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IRequestTrackingService _requestTrackingService;
        readonly CompositeDisposable _disposables = new();

        public RemoteToolRunner(ILogger<RemoteToolRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
        }

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request) => RunCallTool(request, _disposables.ToCancellationToken());
        public async Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposables.ToCancellationToken(), cancellationToken);

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
                        cancellationToken: linkedCts.Token);

                    return responseData.Value ?? ResponseCallTool.Error("Response data is null");
                },
                TimeSpan.FromMinutes(5),
                linkedCts.Token);

            // Wrap the ResponseCallTool back into ResponseData<ResponseCallTool>
            return response.Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request) => RunListTool(request, _disposables.ToCancellationToken());
        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
            => ClientUtils.InvokeAsync<RequestListTool, ResponseListTool[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: Consts.RPC.Client.RunListTool,
                request: request,
                dataArguments: _dataArguments,
                cancellationToken: CancellationTokenSource.CreateLinkedTokenSource(_disposables.ToCancellationToken(), cancellationToken).Token)
                .ContinueWith(task =>
            {
                var response = task.Result;
                if (response.Status == ResponseStatus.Error)
                    return ResponseData<ResponseListTool[]>.Error(request.RequestID, response.Message ?? "Got an error during listing tools");

                return response;
            }, cancellationToken: CancellationTokenSource.CreateLinkedTokenSource(_disposables.ToCancellationToken(), cancellationToken).Token);

        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemoteToolRunner).Name);
            _disposables.Dispose();
        }
    }
}
