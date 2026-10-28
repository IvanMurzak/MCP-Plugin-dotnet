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
    /// <summary>
    /// auth=none (offline / local dev / CI): no credential gate. One editor and one runtime plugin
    /// connection side by side; see <see cref="SingleProjectMcpStrategy"/>.
    /// </summary>
    public class NoAuthMcpStrategy : SingleProjectMcpStrategy
    {
        public override Consts.MCP.Server.AuthOption AuthOption
            => Consts.MCP.Server.AuthOption.none;

        public override void ConfigureAuthentication(TokenAuthenticationOptions options, DataArguments dataArguments)
        {
            // no-auth (offline / local dev / CI): the HTTP endpoint is never token-gated, and the
            // handler runs its anonymous path. No OAuth validation.
            options.OAuthMode = false;
        }
    }
}
