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
                // mcp-authorize b3: oauth mode is the account+instance pairing plane.
                Consts.MCP.Server.AuthOption.oauth => new AccountMcpStrategy(),
                // mcp-authorize b5 (coordinated breaking removal): the legacy shared-token
                // pairing mode (`required`) is deleted. The RS never mints or equality-pairs
                // tokens; it only validates them (oauth) or runs anonymous (none). Any other
                // value — including the retired `required` — fails closed with an explicit error;
                // no silent downgrade to `none`.
                _ => throw new ArgumentException(
                    $"Unsupported auth option: {mode}. " +
                    $"Supported auth options are: {Consts.MCP.Server.AuthOption.none}, {Consts.MCP.Server.AuthOption.oauth}")
            };
        }
    }
}
