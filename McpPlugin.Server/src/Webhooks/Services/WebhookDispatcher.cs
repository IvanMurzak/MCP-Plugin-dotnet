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
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public sealed class WebhookDispatcher : BackgroundService, IWebhookDispatcher
    {
        readonly ILogger<WebhookDispatcher> _logger;
        readonly IHttpClientFactory _httpClientFactory;
        readonly WebhookOptions _options;
        readonly Channel<WebhookMessage> _channel;

        public WebhookDispatcher(
            ILogger<WebhookDispatcher> logger,
            IHttpClientFactory httpClientFactory,
            WebhookOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _channel = Channel.CreateBounded<WebhookMessage>(new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            });
        }

        public bool TryEnqueue(WebhookMessage message)
        {
            return _channel.Writer.TryWrite(message);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _options.LogWarnings(_logger);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogDebug("WebhookDispatcher started.");

            await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DispatchAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch webhook to {Url}.", message.TargetUrl);
                }
            }

            _logger.LogDebug("WebhookDispatcher stopped.");
        }

        async Task DispatchAsync(WebhookMessage message, CancellationToken stoppingToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(_options.TimeoutMs);

            var client = _httpClientFactory.CreateClient("webhook");
            using var content = new StringContent(message.JsonPayload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, message.TargetUrl)
            {
                Content = content
            };

            if (message.HeaderName != null && message.TokenValue != null)
                request.Headers.TryAddWithoutValidation(message.HeaderName, message.TokenValue);

            try
            {
                using var response = await client.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogTrace("Webhook delivered to {Url}. Status: {StatusCode}.", message.TargetUrl, (int)response.StatusCode);
                }
                else
                {
                    _logger.LogWarning("Webhook delivery to {Url} returned {StatusCode}.", message.TargetUrl, (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Webhook delivery to {Url} timed out after {TimeoutMs}ms.", message.TargetUrl, _options.TimeoutMs);
            }
        }
    }
}
