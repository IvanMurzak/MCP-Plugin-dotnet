/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Server
{
    public interface IMcpSessionTracker
    {
        /// <summary>Returns the first connected client across all sessions (no-auth fallback).</summary>
        McpClientData GetClientData();

        /// <summary>Returns the client data for the given physical session ID.</summary>
        McpClientData GetClientData(string physicalId);

        /// <summary>
        /// Returns the first connected client whose routing token matches <paramref name="routingToken"/>.
        /// Falls back to parameterless <see cref="GetClientData()"/> when token is null/empty.
        /// </summary>
        McpClientData GetClientDataByToken(string? routingToken);

        /// <summary>Returns the first connected server data across all sessions (no-auth fallback).</summary>
        McpServerData GetServerData();

        /// <summary>Returns the server data for the given physical session ID.</summary>
        McpServerData GetServerData(string physicalId);

        /// <summary>
        /// Returns the server data for the first session whose routing token matches <paramref name="routingToken"/>.
        /// Falls back to parameterless <see cref="GetServerData()"/> when token is null/empty.
        /// </summary>
        McpServerData GetServerDataByToken(string? routingToken);

        /// <summary>
        /// Returns all client data entries. When <paramref name="routingToken"/> is non-null,
        /// only entries with a matching routing token are returned (scoped to one plugin's group).
        /// </summary>
        IReadOnlyList<McpClientData> GetAllClientData(string? routingToken = null);

        /// <summary>
        /// Creates or updates the session entry keyed by <paramref name="physicalId"/>.
        /// <paramref name="routingToken"/> is the Bearer token used for plugin notification routing;
        /// it may be null in no-auth mode.
        /// </summary>
        void Update(string physicalId, string? routingToken, McpClientData clientData, McpServerData serverData);

        /// <summary>
        /// Increments the reference count for the physical session. Call once per logical
        /// connection (e.g. from McpServerService.StartAsync).
        /// </summary>
        void AddRef(string physicalId);

        /// <summary>
        /// Decrements the reference count. Removes the entry only when the count reaches zero.
        /// Returns <c>true</c> if this was the last reference and the session was removed,
        /// <c>false</c> if other connections with the same physical ID are still active.
        /// </summary>
        bool Remove(string physicalId);
    }
}
