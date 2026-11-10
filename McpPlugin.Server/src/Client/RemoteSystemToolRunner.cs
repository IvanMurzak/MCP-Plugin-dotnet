/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
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
    /// <summary>
    /// Server-side runner for system tools. Uses the request tracking service to support
    /// both synchronous responses and deferred completion via <c>NotifyToolRequestCompleted</c>
    /// (e.g. when a system tool triggers Unity compilation and must wait for it to finish).
    /// </summary>
    public class RemoteSystemToolRunner : IClientSystemToolHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IDataArguments _dataArguments;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IRequestTrackingService _requestTrackingService;
        readonly IMcpConnectionStrategy _strategy;
        readonly CompositeDisposable _disposables = new();
        readonly CancellationTokenSource _cancellationTokenSource;

        public RemoteSystemToolRunner(ILogger<RemoteSystemToolRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IRequestTrackingService requestTrackingService, IMcpConnectionStrategy strategy)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _requestTrackingService = requestTrackingService ?? throw new ArgumentNullException(nameof(requestTrackingService));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();
        }

        /// <summary>
        /// The system tool clients poll as an engine liveness probe (the App every ~30 s, with a 2 s budget).
        /// </summary>
        internal const string LivenessProbeToolName = "ping";

        public async Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            // A liveness probe asks "is the plugin connected right now?": with none connected it answers that at
            // once (Unavailable, HTTP 503) instead of waiting through the reconnect retry ladder, which outlived
            // the probe's own timeout. Other system tools keep waiting for a reconnecting plugin.
            if (request.Name == LivenessProbeToolName
                && _strategy.ResolveConnectionId(McpSessionTokenContext.CurrentToken, 0) == null)
            {
                _logger.LogDebug("System tool '{name}': no engine plugin is connected; answering at once.", request.Name);
                return ResponseCallTool.Error(NotConnectedMessage(), ResponseErrorKind.Unavailable).Pack(request.RequestID);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var toolTimeoutMs = _dataArguments.GetToolTimeoutMs();
            var response = await _requestTrackingService.TrackRequestAsync(
                request.RequestID,
                async () =>
                {
                    // Sent once, never retried: a retry would run the tool a second time.
                    var responseData = await ClientUtils.InvokeOnceAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                        logger: _logger,
                        hubContext: _remoteAppContext,
                        methodName: nameof(IClientSystemToolHub.RunSystemTool),
                        request: request,
                        timeoutMs: toolTimeoutMs,
                        strategy: _strategy,
                        token: McpSessionTokenContext.CurrentToken,
                        cancellationToken: cancellationToken);

                    if (responseData.Status == ResponseStatus.Error)
                    {
                        var errorKind = responseData.ErrorKind == ResponseErrorKind.None
                            ? ResponseErrorKind.Internal
                            : responseData.ErrorKind;
                        return ResponseCallTool.Error(responseData.Message ?? "System tool invocation failed", errorKind, responseData.HttpStatusCode);
                    }

                    return responseData.Value ?? ResponseCallTool.Error("Response data is null", ResponseErrorKind.Internal);
                },
                TimeSpan.FromMilliseconds(toolTimeoutMs),
                cancellationToken);

            return response.Pack(request.RequestID);
        }

        /// <summary>
        /// Why no plugin resolved, in the words the App's probe recognizes: with account routing the
        /// account-empty or pinned-no-match text (<see cref="AgentActionableErrors"/>), else the generic one.
        /// </summary>
        string NotConnectedMessage()
        {
            if (_strategy is not AccountMcpStrategy account)
                return ClientUtils.NoPluginConnectedMessage;

            var identity = McpSessionTokenContext.CurrentIdentity;
            return AgentActionableErrors.ForResolution(
                account.ResolveCurrentSession(), account.Instances, identity?.AccountId, identity?.BoundProjectPin);
        }

        public async Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var response = await ClientUtils.InvokeAsync<RequestListTool, ResponseListTool[], McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: nameof(IClientSystemToolHub.RunListSystemTool),
                request: request,
                dataArguments: _dataArguments,
                strategy: _strategy,
                token: McpSessionTokenContext.CurrentToken,
                cancellationToken: cancellationToken);

            if (response.Status == ResponseStatus.Error)
                return ResponseData<ResponseListTool[]>.Error(request.RequestID, response.Message ?? "Got an error during listing system tools", response.ErrorKind, response.HttpStatusCode);

            return response;
        }

        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemoteSystemToolRunner).Name);
            _disposables.Dispose();
        }
    }
}
