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
using System.Threading.Tasks;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks.Services
{
    /// <summary>
    /// Returns false only for a definitive denial. Dependency failures must throw
    /// <see cref="com.IvanMurzak.McpPlugin.Server.Auth.AuthorizationUnavailableException"/>
    /// so clients can retry without discarding credentials. Caller cancellation must propagate.
    /// </summary>
    public interface IAuthorizationWebhookService
    {
        Task<bool> AuthorizeAiAgentAsync(
            string connectionId,
            string? bearerToken,
            string? remoteIpAddress,
            string? userAgent,
            string? requestPath,
            CancellationToken cancellationToken = default);

        Task<bool> AuthorizePluginAsync(
            string connectionId,
            string? bearerToken,
            string? clientName,
            string? clientVersion,
            CancellationToken cancellationToken = default,
            string? remoteIpAddress = null,
            string? userAgent = null,
            string? requestPath = null);
    }
}
