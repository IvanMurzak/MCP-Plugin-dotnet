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
using System.Threading;
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Validates an opaque (non-JWT) token via OAuth 2.0 Token Introspection (RFC 7662) against the
    /// authorization server (mcp-authorize b2). Results are cached briefly and the client fails
    /// closed on any transport/parse error.
    /// </summary>
    public interface IIntrospectionClient
    {
        Task<IntrospectionResult> IntrospectAsync(string token, CancellationToken cancellationToken);
    }

    /// <summary>Posts the token to the introspection endpoint; returns the raw JSON body, or <c>null</c> on any transport/HTTP error.</summary>
    public delegate Task<string?> IntrospectionPost(string token, CancellationToken cancellationToken);

    /// <summary>Outcome of an introspection call. <see cref="Active"/> is <c>false</c> for inactive tokens AND fail-closed errors.</summary>
    public sealed class IntrospectionResult
    {
        public bool Active { get; }
        public string? Subject { get; }
        public string? Scope { get; }
        public DateTimeOffset? ExpiresAt { get; }

        public IntrospectionResult(bool active, string? subject = null, string? scope = null, DateTimeOffset? expiresAt = null)
        {
            Active = active;
            Subject = subject;
            Scope = scope;
            ExpiresAt = expiresAt;
        }

        public static IntrospectionResult Inactive { get; } = new IntrospectionResult(false);
    }
}
