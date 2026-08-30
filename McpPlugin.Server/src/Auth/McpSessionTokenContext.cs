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
        static readonly AsyncLocal<bool> _isSkillMetaClient = new();
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
        /// <para>Written by <see cref="McpSessionTokenMiddleware"/> per request (reloaded from
        /// <see cref="Tools.ISessionSelectionStore"/>) and by <c>select_engine_instance</c> inside its own
        /// request. On the streamable-HTTP MCP path that per-request reload does NOT reach a request
        /// handler, for the same <c>PerSessionExecutionContext</c> reason documented on
        /// <see cref="CurrentSessionId"/> — so a handler observes this value only within the request
        /// that set it. It is therefore NOT the authority on "what did this session select":
        /// <see cref="Strategy.AccountMcpStrategy"/> treats a null here as "ask the store" and looks the
        /// selection up by <see cref="CurrentSessionId"/>, which is what makes sticky routing work
        /// across requests on that path (issue #195).</para>
        /// </summary>
        public static string? CurrentSelectedInstanceId
        {
            get => _currentSelectedInstanceId.Value;
            set => _currentSelectedInstanceId.Value = value;
        }

        /// <summary>
        /// The MCP session id (mcp-authorize b4) — the sticky-selection key used by
        /// <c>select_engine_instance</c> (see <see cref="Tools.ISessionSelectionStore"/>).
        /// <para>TWO writers, by surface. On the streamable-HTTP MCP path the value a request HANDLER
        /// observes is published once per session by <c>StreamableHttpTransportLayer.RunSessionHandler</c>
        /// from <c>IMcpServer.SessionId</c>: <c>PerSessionExecutionContext</c> is <c>true</c>, so handlers
        /// run on the ExecutionContext captured there and the per-request write below never reaches them.
        /// <see cref="McpSessionTokenMiddleware"/> still captures the <c>Mcp-Session-Id</c> header per
        /// request; that is what the direct-tool REST endpoints observe.</para>
        /// <para>Null for stdio (no HTTP context).</para>
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

        /// <summary>
        /// True when the in-flight request opted in to skill metadata. Set by
        /// <see cref="McpSessionTokenMiddleware"/> from the
        /// <c>X-McpPlugin-Skill-Meta</c> header (see
        /// <c>Consts.MCP.Server.Headers.SkillMetaClient</c>) and consumed by
        /// <c>ExtensionsListMeta.BuildToolMeta</c> to decide whether a
        /// <c>tools/list</c> entry carries <c>_meta.skillDescription</c> /
        /// <c>_meta.skillBody</c>. Cleared in the middleware's <c>finally</c>,
        /// so values never leak across requests.
        /// <para>A SEPARATE flag from <see cref="IsTrustedInternalClient"/> on
        /// purpose: that one also unlocks the <c>Enabled = false</c> catalog, so
        /// a client asking only for skill metadata must not acquire it. Neither
        /// property reads or writes the other's slot.</para>
        /// </summary>
        public static bool IsSkillMetaClient
        {
            get => _isSkillMetaClient.Value;
            set => _isSkillMetaClient.Value = value;
        }
    }
}
