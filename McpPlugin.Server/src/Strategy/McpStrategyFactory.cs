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

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    public interface IMcpStrategyFactory
    {
        IMcpConnectionStrategy Create(Consts.MCP.Server.AuthOption mode);
    }

    public class McpStrategyFactory : IMcpStrategyFactory
    {
        public IMcpConnectionStrategy Create(Consts.MCP.Server.AuthOption mode)
        {
            return mode switch
            {
                Consts.MCP.Server.AuthOption.none => new NoAuthMcpStrategy(),
                Consts.MCP.Server.AuthOption.required => new RequiredAuthMcpStrategy(),
                _ => throw new ArgumentException(
                    $"Unsupported auth option: {mode}. " +
                    $"Supported auth options are: {Consts.MCP.Server.AuthOption.none}, {Consts.MCP.Server.AuthOption.required}")
            };
        }
    }
}
