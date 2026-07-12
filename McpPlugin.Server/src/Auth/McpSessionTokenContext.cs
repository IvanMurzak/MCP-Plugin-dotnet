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
        static readonly AsyncLocal<string?> _currentClientIp = new();
        static readonly AsyncLocal<string?> _currentUserAgent = new();
        static readonly AsyncLocal<bool> _isTrustedInternalClient = new();
        static readonly AsyncLocal<ConnectionIdentity?> _currentIdentity = new();
        static readonly AsyncLocal<string?> _currentProjectPin = new();
        static readonly AsyncLocal<string?> _currentSelectedInstanceId = new();
        static readonly AsyncLocal<string?> _currentSessionId = new();

        public static string? CurrentToken
        {
            get => _currentToken.Value;
            set => _currentToken.Value = value;
        }

        /// <summary>
        /// The resolved <see cref="ConnectionIdentity"/> for the in-flight request in <c>oauth</c>
        /// mode (mcp-authorize b3). Its <see cref="ConnectionIdentity.AccountId"/> is the account
        /// routing key. Null in legacy (token-equality / no-auth) modes and for stdio (no HTTP context).
        /// </summary>
        public static ConnectionIdentity? CurrentIdentity
        {
            get => _currentIdentity.Value;
            set => _currentIdentity.Value = value;
        }

        /// <summary>
        /// The project pin captured for the session (design 04 D14): the <c>/p/&lt;pin&gt;</c> URL
        /// path segment (HTTP) or the <c>project=&lt;pin&gt;</c> stdio spawn arg. A pinned session
        /// routes ONLY to instances whose project path hash matches — never another project.
        /// </summary>
        public static string? CurrentProjectPin
        {
            get => _currentProjectPin.Value;
            set => _currentProjectPin.Value = value;
        }

        /// <summary>
        /// The session's sticky-selected instance id (set by <c>select_engine_instance</c> in b4).
        /// Honored while alive; narrows a pin but never overrides it to a different project. Null in b3.
        /// </summary>
        public static string? CurrentSelectedInstanceId
        {
            get => _currentSelectedInstanceId.Value;
            set => _currentSelectedInstanceId.Value = value;
        }

        /// <summary>
        /// The MCP session id (the <c>Mcp-Session-Id</c> header value) for the in-flight request
        /// (mcp-authorize b4). Captured by <see cref="McpSessionTokenMiddleware"/> and used as the
        /// sticky-selection key by <c>select_engine_instance</c> (see
        /// <see cref="Tools.ISessionSelectionStore"/>). Null on the initialize request (no id yet)
        /// and for stdio (no HTTP context).
        /// </summary>
        public static string? CurrentSessionId
        {
            get => _currentSessionId.Value;
            set => _currentSessionId.Value = value;
        }

        public static string? CurrentClientIp
        {
            get => _currentClientIp.Value;
            set => _currentClientIp.Value = value;
        }

        public static string? CurrentUserAgent
        {
            get => _currentUserAgent.Value;
            set => _currentUserAgent.Value = value;
        }

        /// <summary>
        /// True when the in-flight request was issued by a trusted in-process
        /// client (our own CLI / desktop app). Set by
        /// <see cref="McpSessionTokenMiddleware"/> from the
        /// <c>X-McpPlugin-Internal-Client</c> header (see
        /// <c>Consts.MCP.Server.Headers.TrustedInternalClient</c>) and consumed
        /// by the MCP <c>list</c> routers to decide whether to surface
        /// <c>Enabled = false</c> primitives. Cleared in the middleware's
        /// <c>finally</c>, so values never leak across requests.
        /// </summary>
        public static bool IsTrustedInternalClient
        {
            get => _isTrustedInternalClient.Value;
            set => _isTrustedInternalClient.Value = value;
        }
    }
}
