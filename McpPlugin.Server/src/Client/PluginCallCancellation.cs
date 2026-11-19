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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Tells a plugin to stop a tool or system tool call whose caller went away: the HTTP request was aborted
    /// (<c>HttpContext.RequestAborted</c>) or the MCP client sent <c>notifications/cancelled</c>. Without it the
    /// server only stopped waiting while the editor kept running the call. Sent as the one-way hub message
    /// <see cref="IClientCallCancellation.CancelCall"/>, only to the connection the call was sent to, and only when
    /// that connection's version handshake listed <see cref="Consts.MCP.Plugin.Capabilities.CancelCall"/>: an older
    /// plugin gets nothing (no missing-handler warning) and keeps its previous behaviour. A server timeout sends
    /// nothing either. Keyed by SignalR connection id: recorded at the accepted version handshake, forgotten when
    /// the connection closes.
    /// </summary>
    internal static class PluginCallCancellation
    {
        static readonly ConcurrentDictionary<string, bool> Supported = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Records whether a connection's handshake listed the capability.</summary>
        internal static void Record(string connectionId, string[]? capabilities)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;
            if (capabilities != null && capabilities.Contains(Consts.MCP.Plugin.Capabilities.CancelCall, StringComparer.Ordinal))
                Supported[connectionId] = true;
            else
                Supported.TryRemove(connectionId, out _);
        }

        /// <summary>Forgets a closed connection.</summary>
        internal static void Forget(string connectionId)
        {
            if (!string.IsNullOrEmpty(connectionId))
                Supported.TryRemove(connectionId, out _);
        }

        internal static bool IsSupported(string? connectionId)
            => connectionId != null && Supported.ContainsKey(connectionId);

        /// <summary>
        /// Sends the cancel for <paramref name="requestId"/> to <paramref name="connectionId"/> when it supports it.
        /// Never throws and never waits: the caller is gone, and the plugin answers nothing.
        /// </summary>
        internal static void Send<THub>(ILogger logger, IHubContext<THub> hubContext, string? connectionId, string requestId)
            where THub : Hub
        {
            if (!IsSupported(connectionId) || string.IsNullOrEmpty(requestId))
                return;
            logger.LogInformation("Request '{requestId}': the caller went away; asking plugin connection '{connectionId}' to stop it.",
                requestId, connectionId);
            _ = SendAsync(logger, hubContext, connectionId!, requestId);
        }

        static async Task SendAsync<THub>(ILogger logger, IHubContext<THub> hubContext, string connectionId, string requestId)
            where THub : Hub
        {
            try
            {
                await hubContext.Clients.Client(connectionId)
                    .SendAsync(nameof(IClientCallCancellation.CancelCall), new RequestCancelCall(requestId), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Request '{requestId}': could not send the cancel to plugin connection '{connectionId}'.", requestId, connectionId);
            }
        }
    }
}
