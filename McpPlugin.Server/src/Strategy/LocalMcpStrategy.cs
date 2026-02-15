/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    public class LocalMcpStrategy : IMcpConnectionStrategy
    {
        public Consts.MCP.Server.DeploymentMode DeploymentMode
            => Consts.MCP.Server.DeploymentMode.local;

        public bool AllowMultipleConnections => false;

        public void Validate(DataArguments dataArguments)
        {
            // LOCAL mode: token is optional, no strict validation needed
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            options.ServerToken = dataArguments.Token;
            options.RequireToken = !string.IsNullOrEmpty(dataArguments.Token);
        }
    }
}
