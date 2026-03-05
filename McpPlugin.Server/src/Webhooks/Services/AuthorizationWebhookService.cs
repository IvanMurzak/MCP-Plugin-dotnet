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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Models;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks.Services
{
    public class AuthorizationWebhookService : IAuthorizationWebhookService
    {
        readonly WebhookOptions _options;
        readonly IHttpClientFactory _httpClientFactory;
        readonly ILogger _logger;

        public AuthorizationWebhookService(
            WebhookOptions options,
            IHttpClientFactory httpClientFactory,
            ILogger<AuthorizationWebhookService> logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> AuthorizeAiAgentAsync(
            string connectionId,
            string? bearerToken,
            string? remoteIpAddress,
            string? userAgent,
            string? requestPath,
            CancellationToken cancellationToken = default)
        {
            var request = new AuthorizationRequest
            {
                EventType = "authorization.ai-agent",
                Timestamp = DateTime.UtcNow.ToString("O"),
                ConnectionId = connectionId,
                ClientType = "ai-agent",
                BearerToken = bearerToken,
                RemoteIpAddress = remoteIpAddress,
                UserAgent = userAgent,
                RequestPath = requestPath
            };

            return await SendAuthorizationRequestAsync(request, cancellationToken);
        }

        public async Task<bool> AuthorizePluginAsync(
            string connectionId,
            string? bearerToken,
            string? clientName,
            string? clientVersion,
            CancellationToken cancellationToken = default)
        {
            var request = new AuthorizationRequest
            {
                EventType = "authorization.plugin",
                Timestamp = DateTime.UtcNow.ToString("O"),
                ConnectionId = connectionId,
                ClientType = "plugin",
                BearerToken = bearerToken,
                ClientName = clientName,
                ClientVersion = clientVersion
            };

            return await SendAuthorizationRequestAsync(request, cancellationToken);
        }

        async Task<bool> SendAuthorizationRequestAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("webhook");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_options.TimeoutMs);

                using var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    System.Text.Encoding.UTF8,
                    "application/json");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.AuthorizationWebhookUrl)
                {
                    Content = content
                };

                if (_options.HasToken)
                    httpRequest.Headers.TryAddWithoutValidation(_options.HeaderName, _options.TokenValue);

                using var response = await client.SendAsync(httpRequest, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Authorization webhook returned non-2xx status {StatusCode} for {EventType}",
                        response.StatusCode, request.EventType);
                    return _options.AuthorizationFailOpen;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
                var authResponse = JsonSerializer.Deserialize<AuthorizationResponse>(responseBody);

                if (authResponse == null)
                {
                    _logger.LogWarning(
                        "Authorization webhook response could not be parsed for {EventType}",
                        request.EventType);
                    return _options.AuthorizationFailOpen;
                }

                if (!authResponse.Allowed)
                {
                    _logger.LogWarning(
                        "Authorization webhook denied {EventType}: {Reason}",
                        request.EventType, authResponse.Reason ?? "(no reason provided)");
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout from linked CTS, not external cancellation
                _logger.LogWarning(
                    "Authorization webhook timeout for {EventType}",
                    request.EventType);
                return _options.AuthorizationFailOpen;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Authorization webhook error for {EventType}",
                    request.EventType);
                return _options.AuthorizationFailOpen;
            }
        }
    }
}
