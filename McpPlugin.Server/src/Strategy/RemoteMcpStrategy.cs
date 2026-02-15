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
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server.Strategy
{
    public class RemoteMcpStrategy : IMcpConnectionStrategy
    {
        public Consts.MCP.Server.DeploymentMode DeploymentMode
            => Consts.MCP.Server.DeploymentMode.remote;

        public bool AllowMultipleConnections => true;

        public void Validate(DataArguments dataArguments)
        {
            if (string.IsNullOrEmpty(dataArguments.Token))
            {
                throw new InvalidOperationException(
                    "REMOTE deployment mode requires a token. " +
                    "Set via --token=<value> or MCP_PLUGIN_TOKEN environment variable.");
            }
        }

        public void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            options.ServerToken = dataArguments.Token;
            options.RequireToken = true;
        }
    }
}
