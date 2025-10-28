/*
┌──────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)             │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)    │
│  Copyright (c) 2025 Ivan Murzak                                  │
│  Licensed under the Apache License, Version 2.0.                 │
│  See the LICENSE file in the project root for more information.  │
└──────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public interface IMcpPlugin : IConnection, IDisposableAsync
    {
        ILogger Logger { get; }
        IMcpRunner McpRunner { get; }
        IRpcRouter? RpcRouter { get; }
    }
}
