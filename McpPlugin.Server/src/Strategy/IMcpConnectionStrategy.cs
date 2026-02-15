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
    public interface IMcpConnectionStrategy
    {
        Consts.MCP.Server.DeploymentMode DeploymentMode { get; }

        /// <summary>
        /// Whether this deployment mode allows multiple simultaneous plugin connections.
        /// </summary>
        bool AllowMultipleConnections { get; }

        /// <summary>
        /// Validates the DataArguments configuration for this deployment mode.
        /// Throws if invalid (e.g., REMOTE without token).
        /// </summary>
        void Validate(DataArguments dataArguments);

        /// <summary>
        /// Configures authentication options based on deployment mode.
        /// </summary>
        void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments);
    }
}
