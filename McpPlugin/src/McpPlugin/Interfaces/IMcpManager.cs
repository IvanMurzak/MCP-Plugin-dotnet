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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpManager : IDisposable
    {
        Reflector Reflector { get; }

        /// <summary>Current snapshot of all MCP clients the server reports as active.</summary>
        IReadOnlyList<McpClientData> ActiveClients { get; }

        Observable<Unit> OnForceDisconnect { get; }

        /// <summary>Fires with the newly connected client's data each time a client connects.</summary>
        Observable<McpClientData> OnClientConnected { get; }

        /// <summary>Fires with the disconnected client's data each time a client disconnects.</summary>
        Observable<McpClientData> OnClientDisconnected { get; }

        /// <summary>Fires with the full active-client list every time any client connects or disconnects.</summary>
        Observable<IReadOnlyList<McpClientData>> OnClientsChanged { get; }

        IToolManager? ToolManager { get; }
        IPromptManager? PromptManager { get; }
        IResourceManager? ResourceManager { get; }
        ISystemToolManager? SystemToolManager { get; }
    }
}
