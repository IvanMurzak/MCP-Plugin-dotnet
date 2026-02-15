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
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin.Server.Transport
{
    public interface ITransportFactory
    {
        ITransportLayer Create(Consts.MCP.Server.TransportMethod method);
    }

    public class TransportFactory : ITransportFactory
    {
        public ITransportLayer Create(Consts.MCP.Server.TransportMethod method)
        {
            return method switch
            {
                Consts.MCP.Server.TransportMethod.stdio => new StdioTransportLayer(),
                Consts.MCP.Server.TransportMethod.streamableHttp => new StreamableHttpTransportLayer(),
                _ => throw new ArgumentException(
                    $"Unsupported transport method: {method}. " +
                    $"Supported methods are: {Consts.MCP.Server.TransportMethod.stdio}, {Consts.MCP.Server.TransportMethod.streamableHttp}")
            };
        }
    }
}
