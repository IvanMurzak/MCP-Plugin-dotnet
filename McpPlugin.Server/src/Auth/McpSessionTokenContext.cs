/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Threading;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// Provides ambient access to the current MCP session's auth token.
    /// Set in RunSessionHandler, flows via AsyncLocal through PerSessionExecutionContext.
    /// </summary>
    public static class McpSessionTokenContext
    {
        static readonly AsyncLocal<string?> _currentToken = new();

        public static string? CurrentToken
        {
            get => _currentToken.Value;
            set => _currentToken.Value = value;
        }
    }
}
