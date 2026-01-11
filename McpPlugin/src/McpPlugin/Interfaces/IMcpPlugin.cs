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
        Common.Version Version { get; }
        string BasePath { get; }
        VersionHandshakeResponse? VersionHandshakeStatus { get; }
    }
}
