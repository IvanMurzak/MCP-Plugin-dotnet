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

        /// <summary>The system tool clients poll as an engine liveness probe.</summary>
        internal const string LivenessProbeToolName = "ping";

        /// <summary>
        /// The explicit HTTP status of the probe's "no engine plugin is connected" answer. Unavailable maps to 503
        /// anyway; stating it sets the answer apart from Unavailable errors that are real failures: a plugin
        /// connection that failed after the send (502) and a plugin's own Unavailable error (no status).
        /// </summary>
        const int NotConnectedStatusCode = 503;

        /// <summary>
        /// True when the liveness probe found no plugin to run it: none connected (<see cref="RunSystemTool"/>'s
        /// own answer), or the plugin refused it unrun while reloading (<see cref="ResponseCallTool.NotExecuted"/>,
        /// also Unavailable + 503).
        /// </summary>
        internal static bool IsProbeWithoutPlugin(string name, ResponseData<ResponseCallTool>? response)
            => name == LivenessProbeToolName
               && response?.Status == ResponseStatus.Error
               && response.ErrorKind == ResponseErrorKind.Unavailable
               && response.HttpStatusCode == NotConnectedStatusCode;

        public async Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            // A liveness probe asks "is the plugin connected right now?": with none connected it answers that at
            // once (Unavailable, HTTP 503) instead of waiting through the retry ladder, which outlived the probe's
            // own timeout (the App gives it 2 s). Other system tools keep waiting for a reconnecting plugin.
            var isLivenessProbe = request.Name == LivenessProbeToolName;
            if (isLivenessProbe && NotConnectedMessage() is { } notConnected)
            {
                _logger.LogDebug("System tool '{name}': no engine plugin is connected; answering at once.", request.Name);
                return ResponseCallTool.Error(notConnected, ResponseErrorKind.Unavailable, NotConnectedStatusCode).Pack(request.RequestID);
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var toolTimeoutMs = _dataArguments.GetToolTimeoutMs();
            var response = await _requestTrackingService.TrackRequestAsync(
                request.RequestID,
                async () =>
                {
                    // Sent once, never retried: a retry would run the tool a second time.
                    // A probe whose plugin dropped since the check above still never waits for it.
                    var responseData = await ClientUtils.InvokeCoreAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                        logger: _logger,
                        hubContext: _remoteAppContext,
                        methodName: nameof(IClientSystemToolHub.RunSystemTool),
                        request: request,
                        timeoutMs: toolTimeoutMs,
                        retryAfterDelivery: false,
                        strategy: _strategy,
                        token: McpSessionTokenContext.CurrentToken,
                        cancellationToken: cancellationToken,
                        waitForClient: isLivenessProbe ? CatalogAwareInvoke.NeverWait : null);

                    if (responseData.Status == ResponseStatus.Error)
                    {
                        // A probe whose plugin dropped mid-send gets the same wording as the check above, and the
                        // same status when it was never delivered (one that may have run keeps its 502).
                        if (isLivenessProbe && responseData.ErrorKind == ResponseErrorKind.Unavailable
                            && NotConnectedMessage() is { } droppedMessage)
                            return ResponseCallTool.Error(droppedMessage, ResponseErrorKind.Unavailable,
                                responseData.HttpStatusCode ?? NotConnectedStatusCode);

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
        /// Null while a plugin resolves for the current session. Otherwise why not, in the words the App's probe
        /// recognizes: with account routing the account-empty or pinned-no-match text, else the generic one.
        /// </summary>
        string? NotConnectedMessage()
        {
            if (_strategy is AccountMcpStrategy account)
            {
                var resolution = account.ResolveCurrentSession();
                return resolution.Kind == InstanceResolutionKind.Resolved && resolution.Instance != null
                    ? null
                    : AgentActionableErrors.ForCurrentSession(resolution, account.Instances);
            }

            if (_strategy.ResolveConnectionId(McpSessionTokenContext.CurrentToken, 0) != null)
                return null;
            return (_strategy as SingleProjectMcpStrategy)?.PinnedNotConnectedMessage() ?? ClientUtils.NoPluginConnectedMessage;
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
