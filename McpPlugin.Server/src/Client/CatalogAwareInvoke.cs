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
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// How the remote runners reach a plugin when a <see cref="PluginCatalogCache"/> is available
    /// (without one, both paths behave exactly as before):
    /// <list type="bullet">
    ///   <item><b>Lists</b> never wait for an absent plugin. They answer from the remembered list of a
    ///   reconnecting plugin, or fail at once so the router returns the server's own tools.</item>
    ///   <item><b>Other requests</b> (tool call, prompt get, resource read) wait for a reconnecting
    ///   plugin up to <see cref="PluginCatalogCache.ReconnectWait"/>, then fail with
    ///   <see cref="ReconnectingMessage"/>. With nothing remembered they keep the fixed retry ladder.</item>
    /// </list>
    /// A tool call is still sent at most once: the wait happens before sending.
    /// </summary>
    internal static class CatalogAwareInvoke
    {
        internal const string ReconnectingMessage =
            "The engine plugin is reconnecting or offline, so the request was not sent. " +
            "Retry in a few seconds. If this keeps happening, check that the engine editor is open and connected.";

        internal const string NotConnectedMessage = "No engine plugin is connected.";

        static readonly Func<CancellationToken, Task<bool?>> NeverWait = _ => Task.FromResult<bool?>(false);

        internal static async Task<ResponseData<T>> ListAsync<TRequest, T>(
            ILogger logger,
            IHubContext<McpServerHub> hubContext,
            string methodName,
            TRequest request,
            IDataArguments dataArguments,
            IMcpConnectionStrategy strategy,
            PluginCatalogCache? catalog,
            PluginCatalogKind kind,
            CancellationToken cancellationToken,
            Action<string, ResponseData<T>>? onAnswered = null)
            where TRequest : IRequestID
            where T : class
        {
            // A tools/list answered without the plugin's tools leaves the agent with only the server's
            // own tools, so the plugin's next tool change must reach it (see NoteToolListMissed).
            var noteMiss = catalog != null && kind == PluginCatalogKind.Tools;
            ResponseData<T> response;
            try
            {
                response = await ClientUtils.InvokeCoreAsync<TRequest, T, McpServerHub>(
                    logger, hubContext, methodName, request, dataArguments.PluginTimeoutMs, retryAfterDelivery: true,
                    strategy, McpSessionTokenContext.CurrentToken, cancellationToken,
                    waitForClient: catalog == null ? null : NeverWait,
                    noClientMessage: NotConnectedMessage,
                    onAnswered: (connectionId, answer) =>
                    {
                        if (catalog != null && answer.Status == ResponseStatus.Success && answer.Value != null)
                            catalog.Record(connectionId, kind, answer.Value);
                        onAnswered?.Invoke(connectionId, answer);
                    });
            }
            catch (OperationCanceledException) when (noteMiss)
            {
                catalog!.NoteToolListMissed();
                throw;
            }

            if (catalog != null && response.Status == ResponseStatus.Error && response.ErrorKind == ResponseErrorKind.Unavailable)
            {
                var remembered = catalog.GetOfflineList<T>(kind);
                if (remembered != null)
                {
                    logger.LogDebug("Invoke '{methodName}': the engine plugin is reconnecting; answering from its remembered list.", methodName);
                    return ResponseData<T>.Success(request.RequestID, "The engine plugin is reconnecting; this is its last known list.")
                        .SetData(remembered);
                }
            }
            if (noteMiss && response.Status == ResponseStatus.Error)
                catalog!.NoteToolListMissed();
            return response;
        }

        /// <summary>A non-list request: waits for a reconnecting plugin before sending (see the class summary).</summary>
        internal static Task<ResponseData<TResponse>> SendAsync<TRequest, TResponse>(
            ILogger logger,
            IHubContext<McpServerHub> hubContext,
            string methodName,
            TRequest request,
            int timeoutMs,
            bool retryAfterDelivery,
            IMcpConnectionStrategy strategy,
            PluginCatalogCache? catalog,
            CancellationToken cancellationToken)
            where TRequest : IRequestID
        {
            var token = McpSessionTokenContext.CurrentToken;
            Func<CancellationToken, Task<bool?>>? waitForClient = catalog == null
                ? null
                : ct => catalog.WaitForReconnectAsync(() => strategy.ResolveConnectionId(token, 0) != null, ct);

            return ClientUtils.InvokeCoreAsync<TRequest, TResponse, McpServerHub>(
                logger, hubContext, methodName, request, timeoutMs, retryAfterDelivery,
                strategy, token, cancellationToken,
                waitForClient: waitForClient,
                noClientMessage: ReconnectingMessage);
        }
    }
}
