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
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Builds the OAuth 2.0 Protected Resource Metadata document (RFC 9728) served in <c>oauth</c>
    /// mode (mcp-authorize b2). Names the external authorization server so a spec-compliant MCP
    /// client can run the discovery + authorize flow after a 401 challenge.
    /// </summary>
    public static class OAuthProtectedResourceMetadata
    {
        public static Dictionary<string, object> Build(OAuthResourceServerConfig config)
        {
            return new Dictionary<string, object>
            {
                ["resource"] = config.ResourceUrl,
                ["authorization_servers"] = new[] { config.Issuer },
                ["scopes_supported"] = new[] { OAuthResourceServerConfig.AgentScope },
                ["bearer_methods_supported"] = new[] { "header" }
            };
        }
    }
}
