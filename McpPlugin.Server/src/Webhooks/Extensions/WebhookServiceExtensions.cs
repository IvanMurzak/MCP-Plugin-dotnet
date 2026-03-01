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
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.Extensions.DependencyInjection;

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
                services.AddSingleton<IWebhookEventCollector, NoOpWebhookEventCollector>();
                return services;
            }

            services.AddHttpClient("webhook", client =>
            {
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
            })
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
    }
}
