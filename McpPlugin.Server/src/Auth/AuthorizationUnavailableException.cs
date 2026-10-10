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

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// Authorization could not be checked because a dependency is unavailable. This fails closed,
    /// but must not be treated as evidence that a credential is invalid or revoked.
    /// </summary>
    public sealed class AuthorizationUnavailableException : Exception
    {
        public AuthorizationUnavailableException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
