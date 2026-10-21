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
    /// How the remote runners reach a plugin when a <see cref="PluginCatalogCache"/> is available:
    /// <list type="bullet">
    ///   <item><b>Lists</b> never wait for an absent plugin. They answer from the remembered list of a
    ///   reconnecting plugin, or fail at once so the router returns the server's own tools.</item>
    ///   <item><b>Other requests</b> (tool call, prompt get, resource read) wait for a reconnecting
    ///   plugin up to <see cref="PluginCatalogCache.ReconnectWait"/>, then fail with
    ///   <see cref="ReconnectingMessage"/>. With nothing remembered they keep the old short retry.</item>
    /// </list>
    /// A request is still sent at most once by a tool call: the wait happens before sending.
    /// </summary>
    internal static class CatalogAwareInvoke
    {
        internal const string ReconnectingMessage =
            "The engine plugin is reconnecting or offline, so the request was not sent. " +
            "Retry in a few seconds. If this keeps happening, check that the engine editor is open and connected.";

        internal const string NotConnectedMessage = "No engine plugin is connected.";

        static readonly Func<CancellationToken, Task<bool>> NeverWait = _ => Task.FromResult(false);

        internal static async Task<ResponseData<T>> ListAsync<TRequest, T>(
            ILogger logger,
            IHubContext<McpServerHub> hubContext,
            string methodName,
            TRequest request,
            IDataArguments dataArguments,
            IMcpConnectionStrategy strategy,
            PluginCatalogCache? catalog,
            PluginCatalogKind kind,
            CancellationToken cancellationToken)
            where TRequest : IRequestID
            where T : class
        {
            var token = McpSessionTokenContext.CurrentToken;
            if (catalog == null)
                return await ClientUtils.InvokeAsync<TRequest, T, McpServerHub>(
                    logger, hubContext, methodName, request, dataArguments, strategy, token, cancellationToken);

            var response = await ClientUtils.InvokeCoreAsync<TRequest, T, McpServerHub>(
                logger, hubContext, methodName, request, dataArguments.PluginTimeoutMs, retryAfterDelivery: true,
                strategy, token, cancellationToken,
                waitForClient: NeverWait,
                noClientMessage: NotConnectedMessage,
                onAnswered: (connectionId, answer) =>
                {
                    if (answer.Status == ResponseStatus.Success && answer.Value != null)
                        catalog.Record(connectionId, kind, answer.Value);
                });

            if (response.Status == ResponseStatus.Error && response.ErrorKind == ResponseErrorKind.Unavailable)
            {
                var remembered = catalog.GetOfflineList<T>(kind);
                if (remembered != null)
                {
                    logger.LogDebug("Invoke '{methodName}': the engine plugin is reconnecting; answering from its remembered list.", methodName);
                    return ResponseData<T>.Success(request.RequestID, "The engine plugin is reconnecting; this is its last known list.")
                        .SetData(remembered);
                }
            }
            return response;
        }

        /// <summary>
        /// The <c>waitForClient</c> policy of a non-list request; null (the fixed retry ladder) when
        /// there is no cache.
        /// </summary>
        internal static Func<CancellationToken, Task<bool>>? WaitForPlugin(
            PluginCatalogCache? catalog, IMcpConnectionStrategy strategy, string? token, ILogger logger, string methodName)
        {
            if (catalog == null)
                return null;

            return async cancellationToken =>
            {
                if (catalog.HasOfflineTargetForCurrentSession())
                {
                    logger.LogInformation("Invoke '{methodName}': the engine plugin is reconnecting; waiting up to {wait} for it.",
                        methodName, catalog.ReconnectWait);
                    return await catalog.WaitForReconnectAsync(() => strategy.ResolveConnectionId(token, 0) != null, cancellationToken);
                }

                // Nothing remembered (a plugin that never connected, or the grace is disabled): keep the
                // short fixed retry, bounded by the attempt count in ClientUtils.
                logger.LogWarning("Invoke '{methodName}': No connected clients. Retrying...", methodName);
                await Task.Delay(ClientUtils.RetryDelayMs, cancellationToken);
                return true;
            };
        }
    }
}
