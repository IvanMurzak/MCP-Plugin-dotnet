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
            CancellationToken cancellationToken = default);
    }
}
