/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin.Server.Api
{
    /// <summary>
    /// Shared auth-gating predicate for the REST tool surfaces (<c>/api/tools</c>, <c>/api/system-tools</c>).
    /// Keeps the direct-tool and system-tool endpoint mappers in lockstep so a credential-bearing mode can
    /// never leave one gated and the other open.
    /// </summary>
    internal static class AuthGating
    {
        /// <summary>
        /// True when the REST tool surface must be <c>RequireAuthorization</c>-gated: every credential-bearing
        /// mode — <c>oauth</c>, the offline <c>token</c> (mcp-authorize g6), and the deprecated <c>required</c>
        /// alias. Only <c>none</c> (and the never-configured <c>unknown</c>) leaves it open (fail closed).
        /// </summary>
        public static bool RequiresAuthorization(Consts.MCP.Server.AuthOption mode)
            => mode == Consts.MCP.Server.AuthOption.oauth
            || mode == Consts.MCP.Server.AuthOption.token
            || mode == Consts.MCP.Server.AuthOption.required;
    }
}
