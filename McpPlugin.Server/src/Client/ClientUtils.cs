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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    public static class ClientUtils
    {
        const int maxRetries = 10; // Maximum number of retries
        const int retryDelayMs = 1000; // Delay between retries in milliseconds

        /// <summary>Delay between two attempts to find a plugin connection, in milliseconds.</summary>
        internal const int RetryDelayMs = retryDelayMs;

        // Thread-safe collection to store connected clients, grouped by hub type
        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, bool>> ConnectedClients = new();
        static readonly ConcurrentDictionary<Type, string> LastSuccessfulClients = new();

        // Token-based routing: maps token → connectionId for 1:1 plugin-client pairing
        static readonly ConcurrentDictionary<string, string> TokenToConnectionId = new();
        static readonly ConcurrentDictionary<string, string> ConnectionIdToToken = new();

        static IEnumerable<string> AllConnections => ConnectedClients.TryGetValue(typeof(McpServerHub), out var clients)
            ? clients?.Keys ?? new string[0]
            : Enumerable.Empty<string>();

        public static IEnumerable<string> GetAllConnectionIds(Type hubType)
        {
            if (ConnectedClients.TryGetValue(hubType, out var clients))
                return clients.Keys.ToList();
            return Enumerable.Empty<string>();
        }

        public static string? GetBestConnectionId(Type type, int offset = 0)
        {
            var clients = default(ConcurrentDictionary<string, bool>);
            if (offset == 0)
            {
                if (LastSuccessfulClients.TryGetValue(type, out var connectionId))
                {
                    if (ConnectedClients.TryGetValue(type, out clients) && clients.ContainsKey(connectionId))
                        return connectionId;

                    LastSuccessfulClients.TryRemove(type, out _);
                }
            }
            if (ConnectedClients.TryGetValue(type, out clients))
            {
                var connectionIds = clients.Keys.ToList();
                if (connectionIds.Count == 0)
                    return null;
                return connectionIds[offset % connectionIds.Count];
            }
            return null;
        }
        // static string? FirstConnectionId => ConnectedClients.TryGetValue(typeof(RemoteApp), out var clients)
        //     ? clients?.FirstOrDefault().Key
        //     : null;

        public static void AddClient<T>(string connectionId, ILogger? logger, string? token = null) => AddClient(typeof(T), connectionId, logger, token);
        public static void AddClient(Type type, string connectionId, ILogger? logger, string? token = null)
        {
            var clients = ConnectedClients.GetOrAdd(type, _ => new());
            if (clients.TryAdd(connectionId, true))
            {
                logger?.LogInformation($"Client '{connectionId}' connected to {type.Name}. Total connected clients: {clients.Count}");
            }
            else
            {
                logger?.LogWarning($"Client '{connectionId}' is already connected to {type.Name}.");
            }

            if (!string.IsNullOrEmpty(token))
            {
                TokenToConnectionId[token] = connectionId;
                ConnectionIdToToken[connectionId] = token;
                var tokenHash = TokenHash(token);
                logger?.LogInformation("Token mapping added: hash[{0}] -> connectionId '{1}'", tokenHash, connectionId);
            }
        }
        public static void RemoveClient<T>(string connectionId, ILogger? logger) => RemoveClient(typeof(T), connectionId, logger);
        public static void RemoveClient(Type type, string connectionId, ILogger? logger)
        {
            if (ConnectedClients.TryGetValue(type, out var clients))
            {
                if (clients.TryRemove(connectionId, out _))
                {
                    logger?.LogInformation($"Client '{connectionId}' disconnected from {type.Name}. Total connected clients: {clients.Count}");
                }
                else
                {
                    logger?.LogWarning($"Client '{connectionId}' was not found in connected clients for {type.Name}.");
                }
            }
            else
            {
                logger?.LogWarning($"No connected clients found for {type.Name}.");
            }

            if (ConnectionIdToToken.TryRemove(connectionId, out var removedToken))
            {
                TokenToConnectionId.TryRemove(removedToken, out _);
                var tokenHash = TokenHash(removedToken);
                logger?.LogInformation("Token mapping removed: hash[{0}] for connectionId '{1}'", tokenHash, connectionId);
            }
        }

        private static string TokenHash(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            return BitConverter.ToString(bytes, 0, 4).Replace("-", "").ToLowerInvariant();
        }

        public static string? GetConnectionIdByToken(string? token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            return TokenToConnectionId.TryGetValue(token, out var connectionId) ? connectionId : null;
        }

        public static string? GetTokenByConnectionId(string? connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return null;

            return ConnectionIdToToken.TryGetValue(connectionId, out var token) ? token : null;
        }

        /// <summary>
        /// Invokes an IDEMPOTENT plugin method (tool / prompt / resource listing, prompt get,
        /// resource read). A missing plugin, an error, or a reply slower than
        /// <see cref="IDataArguments.PluginTimeoutMs"/> is retried, up to 10 attempts in total.
        /// Do not use it for tool calls: a retry re-sends a request the plugin may already be
        /// running, so the tool runs twice. Use <see cref="InvokeOnceAsync{TRequest, TResponse, THub}"/>.
        /// </summary>
        public static Task<ResponseData<TResponse>> InvokeAsync<TRequest, TResponse, THub>(
            ILogger logger,
            IHubContext<THub> hubContext,
            string methodName,
            TRequest request,
            IDataArguments dataArguments,
            IMcpConnectionStrategy strategy,
            string? token = null,
            CancellationToken cancellationToken = default)
            where TRequest : IRequestID
            where THub : Hub
            => InvokeCoreAsync<TRequest, TResponse, THub>(logger, hubContext, methodName, request,
                dataArguments.PluginTimeoutMs, retryAfterDelivery: true, strategy, token, cancellationToken);

        /// <summary>
        /// Invokes a NON-idempotent plugin method (a tool call) at most once. While no plugin is
        /// connected it waits and retries like <see cref="InvokeAsync{TRequest, TResponse, THub}"/>
        /// (a Unity domain reload drops the connection for a few seconds). Once the request has been
        /// sent it is never re-sent: no answer within <paramref name="timeoutMs"/> returns a
        /// <see cref="ResponseErrorKind.Timeout"/> error, and a disconnect or failure before the
        /// answer returns <see cref="ResponseErrorKind.Unavailable"/>. In both cases the plugin may
        /// still be running, or may have partially run, the request.
        /// </summary>
        public static Task<ResponseData<TResponse>> InvokeOnceAsync<TRequest, TResponse, THub>(
            ILogger logger,
            IHubContext<THub> hubContext,
            string methodName,
            TRequest request,
            int timeoutMs,
            IMcpConnectionStrategy strategy,
            string? token = null,
            CancellationToken cancellationToken = default)
            where TRequest : IRequestID
            where THub : Hub
            => InvokeCoreAsync<TRequest, TResponse, THub>(logger, hubContext, methodName, request,
                timeoutMs, retryAfterDelivery: false, strategy, token, cancellationToken);

        /// <param name="waitForClient">
        /// What to do when no plugin connection resolves. Null keeps the fixed retry ladder (wait
        /// <see cref="RetryDelayMs"/>, try again, up to 10 attempts). Otherwise it is awaited: true
        /// tries to resolve again (counting as an attempt), false gives up at once with
        /// <paramref name="noClientMessage"/> as a <see cref="ResponseErrorKind.Unavailable"/> error.
        /// </param>
        /// <param name="noClientMessage">The error text when <paramref name="waitForClient"/> gives up.</param>
        /// <param name="onAnswered">Called with the connection that answered and its answer.</param>
        internal static async Task<ResponseData<TResponse>> InvokeCoreAsync<TRequest, TResponse, THub>(
            ILogger logger,
            IHubContext<THub> hubContext,
            string methodName,
            TRequest request,
            int timeoutMs,
            bool retryAfterDelivery,
            IMcpConnectionStrategy strategy,
            string? token,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<bool>>? waitForClient = null,
            string? noClientMessage = null,
            Action<string, ResponseData<TResponse>>? onAnswered = null)
            where TRequest : IRequestID
            where THub : Hub
        {
            if (hubContext == null)
                return ResponseData<TResponse>.Error(request.RequestID, $"'{nameof(hubContext)}' is null.", ResponseErrorKind.Internal).Log(logger);

            if (string.IsNullOrEmpty(methodName))
                return ResponseData<TResponse>.Error(request.RequestID, $"'{nameof(methodName)}' is null.", ResponseErrorKind.Internal).Log(logger);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    var allConnections = string.Join(", ", AllConnections);
                    logger.LogTrace("Invoke '{0}': {1}\nAvailable connections: {2}", methodName, request.ToString(), allConnections);
                }

                var retryCount = 0;
                var sawClient = false;
                var sawTimeout = false;
                while (retryCount < maxRetries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    retryCount++;

                    var connectionId = strategy.ResolveConnectionId(token, retryCount - 1);
                    var client = string.IsNullOrEmpty(connectionId)
                        ? null
                        : hubContext.Clients.Client(connectionId);

                    if (client == null)
                    {
                        if (waitForClient != null)
                        {
                            if (await waitForClient(cancellationToken))
                                continue;
                            return ResponseData<TResponse>.Error(request.RequestID,
                                    noClientMessage ?? $"Invoke '{methodName}': no engine plugin is connected.",
                                    ResponseErrorKind.Unavailable)
                                .Log(logger);
                        }
                        logger.LogWarning("Invoke '{0}': No connected clients. Retrying [{1}/{2}]...", methodName, retryCount, maxRetries);
                        await Task.Delay(retryDelayMs, cancellationToken); // Wait before retrying
                        continue;
                    }
                    sawClient = true;

                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        var allConnections = string.Join(", ", AllConnections);
                        logger.LogTrace("Invoke '{0}': ConnectionId ='{1}'. RequestData:\n{2}\n{3}", methodName, connectionId, request, allConnections);
                    }
                    // Own token per attempt: cancelling it releases an invocation this method gave up on.
                    using var invokeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var invokeTask = client.InvokeAsync<ResponseData<TResponse>>(methodName, request, invokeCts.Token);
                    var completed = await invokeTask.WaitWithTimeout(timeoutMs, cancellationToken);
                    if (completed)
                    {
                        try
                        {
                            var result = await invokeTask;
                            if (result == null)
                                return ResponseData<TResponse>.Error(request.RequestID, $"Invoke '{request}' returned null result.", ResponseErrorKind.Internal)
                                    .Log(logger);

                            LastSuccessfulClients[typeof(McpServerHub)] = connectionId!;
                            if (onAnswered != null)
                            {
                                try { onAnswered(connectionId!, result); }
                                catch (Exception ex) { logger.LogWarning(ex, "Invoke '{methodName}': answer callback failed.", methodName); }
                            }
                            return result;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (HubException ex) when (!retryAfterDelivery)
                        {
                            // The plugin received the call and its handler threw: it answered, the connection is fine.
                            return ResponseData<TResponse>.Error(request.RequestID,
                                    $"Invoke '{methodName}': the plugin failed while handling the request ({ex.Message}).",
                                    ResponseErrorKind.Internal)
                                .Log(logger, ex: ex);
                        }
                        catch (Exception ex) when (!retryAfterDelivery && !IsNotDelivered(ex))
                        {
                            // 502, not Unavailable's default 503: a 503 tells HTTP clients the request was not
                            // processed and is safe to retry, which is exactly what is unknown here.
                            return ResponseData<TResponse>.Error(request.RequestID,
                                    $"Invoke '{methodName}': the plugin connection '{connectionId}' failed after the request was sent ({ex.Message}). " +
                                    "The request was not re-sent, because the plugin may already have run it.",
                                    ResponseErrorKind.Unavailable,
                                    httpStatusCode: MaybeDeliveredStatusCode)
                                .Log(logger, ex: ex);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"Invoke '{methodName}': Error invoking '{request}' on client '{connectionId}': {ex.Message}");
                            await Task.Delay(50, cancellationToken); // Wait before retrying
                            continue;
                        }
                    }

                    // Timeout occurred
                    invokeCts.Cancel();
                    ObserveFault(invokeTask); // abandoned: a late connection failure must not surface as unobserved
                    if (!retryAfterDelivery)
                    {
                        return ResponseData<TResponse>.Error(request.RequestID,
                                $"Invoke '{methodName}': Timeout: client '{connectionId}' did not answer within {timeoutMs} ms. " +
                                "The request was not re-sent, because the plugin may still be running it.",
                                ResponseErrorKind.Timeout)
                            .Log(logger);
                    }
                    sawTimeout = true;
                    logger.LogWarning("Invoke '{methodName}': Timeout: Client '{connectionId}' did not respond in {timeoutMs} ms. Retrying [{retryCount}/{maxRetries}].",
                        methodName, connectionId, timeoutMs, retryCount, maxRetries);
                    await Task.Delay(retryDelayMs, cancellationToken); // Wait before retrying
                    // Restart the loop to try again with a new client
                }
                var errorKind = sawTimeout
                    ? ResponseErrorKind.Timeout
                    : sawClient ? ResponseErrorKind.Internal : ResponseErrorKind.Unavailable;
                return ResponseData<TResponse>.Error(request.RequestID, $"Invoke '{methodName}': Failed to invoke '{request}' after {retryCount} retries.", errorKind)
                    .Log(logger);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Invoke '{methodName}': Request cancelled by the caller.", methodName);
                throw;
            }
            catch (Exception ex)
            {
                return ResponseData<TResponse>.Error(request.RequestID, $"Invoke '{methodName}': Failed to invoke '{request}'. Exception: {ex}", ResponseErrorKind.Internal)
                    .Log(logger, ex: ex);
            }
        }

        /// <summary>
        /// True only when SignalR refused the call because the connection was already gone, so the
        /// request never left the server and sending it again cannot run it twice. Any other
        /// failure may have happened after delivery.
        /// </summary>
        static bool IsNotDelivered(Exception ex)
            => ex is IOException && ex.Message.Contains("does not exist", StringComparison.Ordinal);

        /// <summary>HTTP status for a tool call that failed after it may have reached the plugin.</summary>
        internal const int MaybeDeliveredStatusCode = 502;

        static void ObserveFault(Task task)
            => task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
