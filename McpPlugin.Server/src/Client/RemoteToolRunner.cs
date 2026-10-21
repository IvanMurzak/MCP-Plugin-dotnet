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
using com.IvanMurzak.McpPlugin.Server.Tools;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    public class RemoteToolRunner : IClientToolHub, ICancellableToolHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IDataArguments _dataArguments;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IRequestTrackingService _requestTrackingService;
        readonly IMcpConnectionStrategy _strategy;
        readonly CompositeDisposable _disposables = new();
        readonly CancellationTokenSource _cancellationTokenSource;
        readonly PluginCatalogCache? _catalog;

        public RemoteToolRunner(ILogger<RemoteToolRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService, IMcpConnectionStrategy strategy)
            : this(logger, remoteAppContext, dataArguments, requestTrackingService, strategy, catalog: null)
        {
        }

        /// <summary>
        /// With a <paramref name="catalog"/>, a reconnecting plugin's remembered tools answer
        /// <c>tools/list</c>, a tool call waits for the plugin to return, and a list never waits for a
        /// plugin that is not there (see <see cref="CatalogAwareInvoke"/>).
        /// </summary>
        public RemoteToolRunner(ILogger<RemoteToolRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService, IMcpConnectionStrategy strategy, PluginCatalogCache? catalog)
        {
            _catalog = catalog;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();
        }

        public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request) => RunCallTool(request, default);
        public async Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var toolTimeoutMs = _dataArguments.GetToolTimeoutMs();
            var response = await _requestTrackingService.TrackRequestAsync(
                request.RequestID,
                async () =>
                {
                    // Sent once, never retried: a retry would run the tool a second time. Waiting for a
                    // reconnecting plugin happens BEFORE sending, so it cannot cause a second run.
                    var token = McpSessionTokenContext.CurrentToken;
                    var responseData = await ClientUtils.InvokeCoreAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                        logger: _logger,
                        hubContext: _remoteAppContext,
                        methodName: nameof(IClientToolHub.RunCallTool),
                        request: request,
                        timeoutMs: toolTimeoutMs,
                        retryAfterDelivery: false,
                        strategy: _strategy,
                        token: token,
                        cancellationToken: cancellationToken,
                        waitForClient: CatalogAwareInvoke.WaitForPlugin(_catalog, _strategy, token, _logger, nameof(IClientToolHub.RunCallTool)),
                        noClientMessage: CatalogAwareInvoke.ReconnectingMessage);

                    if (responseData.Status == ResponseStatus.Error)
                    {
                        var errorKind = responseData.ErrorKind == ResponseErrorKind.None
                            ? ResponseErrorKind.Internal
                            : responseData.ErrorKind;
                        return ResponseCallTool.Error(responseData.Message ?? "Tool invocation failed", errorKind, responseData.HttpStatusCode);
                    }

                    return responseData.Value ?? ResponseCallTool.Error("Response data is null", ResponseErrorKind.Internal);
                },
                TimeSpan.FromMilliseconds(toolTimeoutMs),
                cancellationToken);

            // Wrap the ResponseCallTool back into ResponseData<ResponseCallTool>
            return response.Pack(request.RequestID);
        }

        public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request) => RunListTool(request, default);
        public async Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var response = await CatalogAwareInvoke.ListAsync<RequestListTool, ResponseListTool[]>(
                _logger, _remoteAppContext, nameof(IClientToolHub.RunListTool), request, _dataArguments,
                _strategy, _catalog, PluginCatalogKind.Tools, cancellationToken);

            if (response.Status == ResponseStatus.Error)
                return ResponseData<ResponseListTool[]>.Error(request.RequestID, response.Message ?? "Got an error during listing tools", response.ErrorKind, response.HttpStatusCode);

            return response;
        }

        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemoteToolRunner).Name);
            _disposables.Dispose();
        }
    }
}
