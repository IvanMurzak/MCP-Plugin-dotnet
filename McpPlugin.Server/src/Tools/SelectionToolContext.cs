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
using com.IvanMurzak.McpPlugin.Server.Auth;

namespace com.IvanMurzak.McpPlugin.Server.Tools
{
    /// <summary>
    /// The per-request session facts a server-native tool needs (mcp-authorize b4). Materialized from
    /// the ambient <see cref="McpSessionTokenContext"/> at call time by <see cref="FromCurrent"/>, or
    /// built explicitly in tests. Kept as a plain value so the tool logic is unit-testable without the
    /// AsyncLocal request context.
    /// </summary>
    public readonly struct SelectionToolContext
    {
        /// <summary>The account routing key (JWT <c>sub</c> / introspected PAT owner). Null when unauthenticated.</summary>
        public string? AccountId { get; }

        /// <summary>The MCP session id (<c>Mcp-Session-Id</c>) — the sticky-selection key. Null on the initialize request / stdio.</summary>
        public string? SessionId { get; }

        /// <summary>The session's strict project pin (design 04 D14), or null when unpinned.</summary>
        public string? ProjectPin { get; }

        /// <summary>The raw bearer credential (JWT or PAT) forwarded verbatim to the AS on enroll. Never logged.</summary>
        public string? Bearer { get; }

        public SelectionToolContext(string? accountId, string? sessionId, string? projectPin, string? bearer)
        {
            AccountId = accountId;
            SessionId = sessionId;
            ProjectPin = projectPin;
            Bearer = bearer;
        }

        /// <summary>Snapshot the current request's ambient session context.</summary>
        public static SelectionToolContext FromCurrent()
            => new SelectionToolContext(
                McpSessionTokenContext.CurrentIdentity?.AccountId,
                McpSessionTokenContext.CurrentSessionId,
                McpSessionTokenContext.CurrentProjectPin,
                McpSessionTokenContext.CurrentToken);
    }
}
