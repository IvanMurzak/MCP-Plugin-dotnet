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
using System;
using System.Security.Claims;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// The resolved identity of a single MCP connection (agent session or engine-plugin connection)
    /// in <c>oauth</c> mode (mcp-authorize b3). Replaces the raw bearer-token string as the routing
    /// key: every inbound credential resolves to a <see cref="ConnectionIdentity"/> whose
    /// <see cref="AccountId"/> (the JWT <c>sub</c> / introspected PAT owner) is <b>THE routing key</b>
    /// for the account+instance pairing plane (see design doc 04). Carried through
    /// <see cref="McpSessionTokenContext"/> so downstream routing (<c>AccountMcpStrategy</c>) never
    /// consults a token string again.
    /// </summary>
    public sealed record ConnectionIdentity
    {
        /// <summary>OAuth scope value marking an AI-agent session.</summary>
        public const string ScopeAgent = "mcp:agent";

        /// <summary>OAuth scope value marking an engine-plugin connection.</summary>
        public const string ScopePlugin = "mcp:plugin";

        /// <summary>Role of an AI-agent session.</summary>
        public const string RoleAgent = "agent";

        /// <summary>Role of an engine-plugin connection.</summary>
        public const string RolePlugin = "plugin";

        /// <summary>Role assigned when the scope carries neither the agent nor the plugin marker.</summary>
        public const string RoleUnknown = "unknown";

        /// <summary>
        /// The account id — the JWT <c>sub</c> claim or the introspected PAT owner. THE routing key:
        /// all registry buckets, routing, notifications and session data are keyed by this value.
        /// Never null/empty for a successfully-resolved identity.
        /// </summary>
        public string AccountId { get; }

        /// <summary><c>"agent"</c> | <c>"plugin"</c> | <c>"unknown"</c>, derived from the token scope.</summary>
        public string Role { get; }

        /// <summary>The DCR client / device / PAT id (audit only — never a routing key). May be null.</summary>
        public string? ClientId { get; }

        /// <summary>
        /// Token expiry, when the validator surfaced it. Audit only; null when unknown (the b2
        /// validator does not yet expose <c>exp</c> — wired when it does, without changing routing).
        /// </summary>
        public DateTimeOffset? Exp { get; }

        public ConnectionIdentity(string accountId, string role, string? clientId = null, DateTimeOffset? exp = null)
        {
            if (string.IsNullOrEmpty(accountId))
                throw new ArgumentException("Account id (sub) must be non-empty for a resolved identity.", nameof(accountId));
            AccountId = accountId;
            Role = string.IsNullOrEmpty(role) ? RoleUnknown : role;
            ClientId = clientId;
            Exp = exp;
        }

        public bool IsAgent => string.Equals(Role, RoleAgent, StringComparison.Ordinal);
        public bool IsPlugin => string.Equals(Role, RolePlugin, StringComparison.Ordinal);

        /// <summary>Maps a space-delimited OAuth <c>scope</c> string to a role.</summary>
        public static string RoleFromScope(string? scope)
        {
            if (string.IsNullOrEmpty(scope))
                return RoleUnknown;
            foreach (var s in scope!.Split(' '))
            {
                if (string.Equals(s, ScopePlugin, StringComparison.Ordinal))
                    return RolePlugin;
            }
            foreach (var s in scope!.Split(' '))
            {
                if (string.Equals(s, ScopeAgent, StringComparison.Ordinal))
                    return RoleAgent;
            }
            return RoleUnknown;
        }

        /// <summary>
        /// Builds a <see cref="ConnectionIdentity"/> from raw claim values. Returns null when
        /// <paramref name="subject"/> is null/empty — an identity with no account cannot route.
        /// </summary>
        public static ConnectionIdentity? Create(string? subject, string? scope, string? clientId = null, DateTimeOffset? exp = null)
        {
            if (string.IsNullOrEmpty(subject))
                return null;
            return new ConnectionIdentity(subject!, RoleFromScope(scope), clientId, exp);
        }

        /// <summary>
        /// Builds a <see cref="ConnectionIdentity"/> from a validated <see cref="ClaimsPrincipal"/>
        /// (the ticket the <see cref="TokenAuthenticationHandler"/> OAuth path issues). Returns null
        /// when the principal carries no <c>sub</c> claim.
        /// </summary>
        public static ConnectionIdentity? FromPrincipal(ClaimsPrincipal? principal)
        {
            if (principal == null)
                return null;
            var sub = principal.FindFirst(TokenAuthenticationHandler.SubjectClaimType)?.Value
                   ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var scope = principal.FindFirst(TokenAuthenticationHandler.ScopeClaimType)?.Value;
            var clientId = principal.FindFirst(TokenAuthenticationHandler.ClientIdClaimType)?.Value;
            return Create(sub, scope, clientId);
        }
    }
}
