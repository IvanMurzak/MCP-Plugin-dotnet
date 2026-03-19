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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using R3;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Server-side runner for system tools. Unlike regular tools (which use the request
    /// tracking service for deferred completion via <c>NotifyToolRequestCompleted</c>),
    /// system tools return their response directly via SignalR <c>InvokeAsync</c>.
    /// </summary>
    public class RemoteSystemToolRunner : IClientSystemToolHub, IDisposable
    {
        readonly ILogger _logger;
        readonly IDataArguments _dataArguments;
        readonly IHubContext<McpServerHub> _remoteAppContext;
        readonly IMcpConnectionStrategy _strategy;
        readonly CompositeDisposable _disposables = new();
        readonly CancellationTokenSource _cancellationTokenSource;

        public RemoteSystemToolRunner(ILogger<RemoteSystemToolRunner> logger, IHubContext<McpServerHub> remoteAppContext, IDataArguments dataArguments, IMcpConnectionStrategy strategy)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogTrace("Ctor.");
            _dataArguments = dataArguments ?? throw new ArgumentNullException(nameof(dataArguments));
            _remoteAppContext = remoteAppContext ?? throw new ArgumentNullException(nameof(remoteAppContext));
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _cancellationTokenSource = _disposables.ToCancellationTokenSource();
        }

        public async Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
            cancellationToken = linkedCts.Token;

            var response = await ClientUtils.InvokeAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                logger: _logger,
                hubContext: _remoteAppContext,
                methodName: nameof(IClientSystemToolHub.RunSystemTool),
                request: request,
                dataArguments: _dataArguments,
                strategy: _strategy,
                token: McpSessionTokenContext.CurrentToken,
                cancellationToken: cancellationToken);

            if (response.Status == ResponseStatus.Error)
                return ResponseData<ResponseCallTool>.Error(request.RequestID, response.Message ?? "System tool execution failed.");

            return response;
        }

        public void Dispose()
        {
            _logger.LogTrace("{0} Dispose.", typeof(RemoteSystemToolRunner).Name);
            _disposables.Dispose();
        }
    }
}
