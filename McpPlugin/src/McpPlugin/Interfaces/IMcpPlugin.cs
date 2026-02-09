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
using Microsoft.Extensions.Logging;
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpPlugin : IConnection, IDisposable
    {
        ILogger Logger { get; }
        IMcpManager McpManager { get; }
        IRemoteMcpManagerHub? RemoteMcpManagerHub { get; }
        /// <summary>
        /// Gets the version of the MCP plugin.
        /// </summary>
        Common.Version Version { get; }
        /// <summary>
        /// Gets the current base directory path of the MCP plugin.
        /// </summary>
        string CurrentBaseDirectory { get; }
        /// <summary>
        /// Gets the version handshake response status if a handshake has been performed; otherwise, null.
        /// </summary>
        VersionHandshakeResponse? VersionHandshakeStatus { get; }
        /// <summary>
        /// Gets the total number of tool calls made.
        /// </summary>
        long ToolCallCount => 0;
    }
}
