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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Webhooks
{
    public static class WebhookServiceExtensions
    {
        public static IServiceCollection AddWebhooks(this IServiceCollection services, IDataArguments dataArguments)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            if (dataArguments == null)
                throw new ArgumentNullException(nameof(dataArguments));

            var options = WebhookOptions.FromDataArguments(dataArguments);

            services.AddSingleton(options);

            if (!options.IsEnabled)
            {
                services.AddSingleton<IWebhookDispatcher, NoOpWebhookDispatcher>();
                services.AddSingleton<IWebhookEventCollector, NoOpWebhookEventCollector>();
                // Log any invalid URL warnings even when webhooks are otherwise disabled
                if (options.HasInvalidUrls)
                    services.AddHostedService(sp => new WebhookWarningLogger(
                        sp.GetRequiredService<WebhookOptions>(),
                        sp.GetRequiredService<ILogger<WebhookWarningLogger>>()));
                return services;
            }

            services.AddHttpClient("webhook")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(2)
            });

            services.AddSingleton<WebhookDispatcher>();
            services.AddSingleton<IWebhookDispatcher>(sp => sp.GetRequiredService<WebhookDispatcher>());
            services.AddHostedService(sp => sp.GetRequiredService<WebhookDispatcher>());

            services.AddSingleton<IWebhookEventCollector, WebhookEventCollector>();

            return services;
        }

        sealed class WebhookWarningLogger : IHostedService
        {
            readonly WebhookOptions _options;
            readonly ILogger _logger;

            public WebhookWarningLogger(WebhookOptions options, ILogger<WebhookWarningLogger> logger)
            {
                _options = options;
                _logger = logger;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _options.LogWarnings(_logger);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
