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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.ReflectorNet;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace com.IvanMurzak.McpPlugin
{
    public class HubConnectionProvider : IHubConnectionProvider
    {
        private readonly ILogger _logger;
        private readonly Reflector _reflector;
        private readonly IServiceProvider _serviceProvider;

        public HubConnectionProvider(ILogger<HubConnection> logger, Reflector reflector, IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public Task<HubConnection> CreateConnectionAsync(string endpoint)
        {
            _logger.LogInformation($"Creating HubConnection to {endpoint}");

            try
            {
                var connectionConfig = _serviceProvider.GetRequiredService<IOptions<ConnectionConfig>>().Value;

                var hubConnectionBuilder = new HubConnectionBuilder()
                    .WithUrl(connectionConfig.Host + endpoint, options =>
                    {
                        if (!string.IsNullOrEmpty(connectionConfig.Token))
                            options.AccessTokenProvider = () => Task.FromResult<string?>(connectionConfig.Token);
                    })
                    .WithAutomaticReconnect(new FixedRetryPolicy(TimeSpan.FromSeconds(10)))
                    .WithKeepAliveInterval(TimeSpan.FromSeconds(30))
                    .WithServerTimeout(TimeSpan.FromMinutes(5))
                    .AddJsonProtocol(options => SignalR_JsonConfiguration.ConfigureJsonSerializer(_reflector, options))
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.AddProvider(new ForwardLoggerProvider(_logger,
                            additionalErrorMessage: "To stop seeing the error, please <b>Stop</b> the connection to MCP server in <b>AI Game Developer</b> window."));
                        logging.SetMinimumLevel(LogLevel.Trace);
                    });

                var hubConnection = hubConnectionBuilder.Build();

                return Task.FromResult(hubConnection);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to create HubConnection. Exception: {ex.Message}");
                if (ex.InnerException != null)
                    _logger.LogError($"Inner Exception: {ex.InnerException.Message}");
                if (ex is TypeInitializationException tie && tie.InnerException != null)
                    _logger.LogError($"TypeInitializer Inner Exception: {tie.InnerException}");
                throw;
            }
        }
    }
}
