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
            switch (mode)
            {
                case Consts.MCP.Server.AuthOption.none:
                    return new NoAuthMcpStrategy();

                // mcp-authorize b3: oauth mode is the account+instance pairing plane.
                case Consts.MCP.Server.AuthOption.oauth:
                    return new AccountMcpStrategy();

                // mcp-authorize g6: offline shared-secret mode — a loopback single-project server
                // gated on a single static token, validated with a constant-time compare.
                case Consts.MCP.Server.AuthOption.token:
                    return new LocalTokenMcpStrategy();

                // mcp-authorize g6 back-compat alias: an un-migrated binary still emitting
                // `authorization=required` (+ token) runs token-gated instead of crashing. This is a
                // strict superset that ALSO resolves the g5 boot crash for un-migrated configs — never
                // a silent downgrade to anonymous (which dropping `required` would cause). Deprecated:
                // migrate the config to `auth=token`.
                case Consts.MCP.Server.AuthOption.required:
                    NLog.LogManager.GetCurrentClassLogger().Warn(
                        "auth=required is deprecated (mcp-authorize g6): aliasing it onto the offline 'token' strategy. Migrate the configuration to 'auth=token'.");
                    return new LocalTokenMcpStrategy();

                // Any other value — including the retired `unknown` — fails closed with an explicit
                // error; no silent downgrade to `none`.
                default:
                    throw new ArgumentException(
                        $"Unsupported auth option: {mode}. " +
                        $"Supported auth options are: {Consts.MCP.Server.AuthOption.none}, {Consts.MCP.Server.AuthOption.oauth}, {Consts.MCP.Server.AuthOption.token} (and the deprecated {Consts.MCP.Server.AuthOption.required} alias).");
            }
        }
    }
}
