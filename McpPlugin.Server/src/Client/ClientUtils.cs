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
using System.Linq;
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
                logger?.LogInformation("Token mapping added: token -> connectionId '{0}'", connectionId);
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
                logger?.LogInformation("Token mapping removed for connectionId '{0}'", connectionId);
            }
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

        public static async Task<ResponseData<TResponse>> InvokeAsync<TRequest, TResponse, THub>(
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
        {
            if (hubContext == null)
                return ResponseData<TResponse>.Error(request.RequestID, $"'{nameof(hubContext)}' is null.").Log(logger);

            if (string.IsNullOrEmpty(methodName))
                return ResponseData<TResponse>.Error(request.RequestID, $"'{nameof(methodName)}' is null.").Log(logger);

            try
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    var allConnections = string.Join(", ", AllConnections);
                    logger.LogTrace("Invoke '{0}': {1}\nAvailable connections: {2}", methodName, request.ToString(), allConnections);
                }

                var retryCount = 0;
                while (retryCount < maxRetries)
                {
                    retryCount++;

                    var connectionId = strategy.ResolveConnectionId(token, retryCount - 1);
                    var client = string.IsNullOrEmpty(connectionId)
                        ? null
                        : hubContext.Clients.Client(connectionId);

                    if (client == null)
                    {
                        logger.LogWarning("No connected clients. Retrying [{0}/{1}]...", retryCount, maxRetries);
                        await Task.Delay(2500, cancellationToken); // Wait before retrying
                        continue;
                    }

                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        var allConnections = string.Join(", ", AllConnections);
                        logger.LogTrace("Invoke '{0}', ConnectionId ='{1}'. RequestData:\n{2}\n{3}", methodName, connectionId, request, allConnections);
                    }
                    var invokeTask = client.InvokeAsync<ResponseData<TResponse>>(methodName, request, cancellationToken);
                    var completed = await invokeTask.WaitWithTimeout(dataArguments.PluginTimeoutMs, cancellationToken);
                    if (completed)
                    {
                        try
                        {
                            var result = await invokeTask;
                            if (result == null)
                                return ResponseData<TResponse>.Error(request.RequestID, $"Invoke '{request}' returned null result.")
                                    .Log(logger);

                            LastSuccessfulClients[typeof(McpServerHub)] = connectionId!;
                            return result;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"Error invoking '{request}' on client '{connectionId}': {ex.Message}");
                            // RemoveCurrentClient(client);
                            await Task.Delay(50, cancellationToken); // Wait before retrying
                            continue;
                        }
                    }

                    // Timeout occurred
                    logger.LogWarning($"Timeout: Client '{connectionId}' did not respond in {dataArguments.PluginTimeoutMs} ms. Removing from ConnectedClients.");
                    // RemoveCurrentClient(client);
                    await Task.Delay(retryDelayMs, cancellationToken); // Wait before retrying
                    // Restart the loop to try again with a new client
                }
                return ResponseData<TResponse>.Error(request.RequestID, $"Failed to invoke '{request}' after {retryCount} retries.")
                    .Log(logger);
            }
            catch (Exception ex)
            {
                return ResponseData<TResponse>.Error(request.RequestID, $"Failed to invoke '{request}'. Exception: {ex}")
                    .Log(logger, ex: ex);
            }
        }
    }
}
