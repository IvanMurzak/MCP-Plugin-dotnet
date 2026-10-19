/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
namespace com.IvanMurzak.McpPlugin.Common
{
    public static partial class Consts
    {
        public static class Hub
        {
            public const int DefaultPort = 8080;
            public const int MaxPort = 65535;
            public const string DefaultHost = "http://localhost:8080";
            public const string RemoteApp = "/hub/mcp-server";
            public const int DefaultTimeoutMs = 10000;

            /// <summary>
            /// Default time the server waits for ONE tool call to answer (5 minutes). A tool call is
            /// sent once and never retried, so this bounds the whole call, not one attempt.
            /// </summary>
            public const int DefaultToolTimeoutMs = 300000;
        }
    }
}
