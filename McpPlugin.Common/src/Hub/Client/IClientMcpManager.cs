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
    public interface IClientMcpManager : IClientDisconnectable
    {
        IClientToolHub? ToolHub { get; }
        IClientPromptHub? PromptHub { get; }
        IClientResourceHub? ResourceHub { get; }

        Task OnMcpClientConnected(McpClientData connectedClient, McpClientData[] allActiveClients);
        Task OnMcpClientDisconnected(McpClientData disconnectedClient, McpClientData[] remainingClients);

        /// <summary>
        /// Called once on initial connection to modifie ActiveClients with the server's current
        /// snapshot, covering the edge case where clients were already connected before the plugin joined.
        /// </summary>
        Task OnInitialClientData(McpClientData[] allActiveClients);
    }
}
