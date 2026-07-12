/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The plugin's account sign-in state, surfaced by <see cref="PluginCredentialProvider"/> so engine UI
    /// can render a sign-in chip without knowing anything about tokens (mcp-authorize b7, design 06).
    /// </summary>
    public enum AuthState
    {
        /// <summary>No credential is present (never signed in, or explicitly signed out).</summary>
        SignedOut,

        /// <summary>A valid (or refreshable) credential is present — connect signed-in.</summary>
        SignedIn,

        /// <summary>
        /// A credential was present but a refresh failed (expired/revoked refresh token). The connection
        /// cannot recover automatically; the engine UI must prompt "Session expired — sign in again".
        /// </summary>
        SignInRequired
    }
}
