/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Common.Hub.Client
{
    public interface IClientMcpRpc : IClientDisconnectable
    {
        // Task ForceDisconnect(); // Inherited from IClientDisconnectable

        /// <summary>
        /// Fired when an MCP client connects. Carries the newly connected client's data and
        /// the complete list of all currently active clients (including the new one).
        /// </summary>
        Task OnMcpClientConnected(McpClientData connectedClient, McpClientData[] allActiveClients);

        /// <summary>
        /// Fired when an MCP client disconnects. Carries the disconnected client's data and
        /// the complete list of clients still active after the disconnection.
        /// </summary>
        Task OnMcpClientDisconnected(McpClientData disconnectedClient, McpClientData[] remainingClients);
    }
}
