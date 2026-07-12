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
#if NET5_0_OR_GREATER
using System.Net.Http;
#endif
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

                // Instance-metadata handshake (mcp-authorize b7): the non-secret identity fields ride as
                // query parameters so the server can register this editor session in its account+instance
                // pairing plane (b3). The credential (JWT) itself NEVER goes in the query — it is presented
                // via AccessTokenProvider (the Authorization header) below.
                var baseUrl = connectionConfig.Host + endpoint;
                var url = connectionConfig.InstanceMetadata?.AppendToUrl(baseUrl) ?? baseUrl;

                var hubConnectionBuilder = new HubConnectionBuilder()
                    .WithUrl(url, options =>
                    {
                        // Credential-provider callback (mcp-authorize b7): fetches the current, auto-refreshed
                        // account JWT on every (re)connect. Null provider ⇒ anonymous (none-mode local server).
                        // SignalR places this token in the Authorization header for both the negotiate request
                        // and the WebSocket upgrade — the server reads it there (never a query param).
                        // Assign the credential-provider delegate directly (null ⇒ a constant null-token
                        // provider for an anonymous none-mode connection). SignalR invokes the delegate on
                        // every (re)connect, so a proactively-refreshed JWT is always fetched fresh — no
                        // wrapping lambda needed.
                        options.AccessTokenProvider = connectionConfig.CredentialProvider
                            ?? (() => Task.FromResult<string?>(null));

#if NET5_0_OR_GREATER
                        // OPT-IN transport CONNECT timeout (ConnectionConfig.ConnectTimeoutSeconds > 0): make an
                        // unreachable / black-holed endpoint fail in a few seconds instead of hanging ~30s on the OS
                        // TCP connect. With the opt-in bounded reconnect (MaxConsecutiveConnectionFailures), this lets
                        // an unreachable server settle quickly into idle-Disconnected instead of keeping a long
                        // negotiate in-flight — a recurring godotengine/godot#78513 hot-reload pin for a collectible-
                        // ALC consumer (the Godot addon opts in). Default (0) leaves the framework default, so
                        // Unity/Unreal are unchanged. SocketsHttpHandler is a .NET Core type (absent on the
                        // netstandard2.1 / Unity target), so this is .NET-Core-only.
                        var connectTimeoutSeconds = connectionConfig.ConnectTimeoutSeconds;
                        if (connectTimeoutSeconds > 0)
                        {
                            options.HttpMessageHandlerFactory = inner =>
                            {
                                if (inner is SocketsHttpHandler sockets)
                                    sockets.ConnectTimeout = TimeSpan.FromSeconds(connectTimeoutSeconds);
                                return inner;
                            };
                        }
#endif
                    })
                    .WithAutomaticReconnect(new FixedRetryPolicy(TimeSpan.FromSeconds(10), maxRetries: 3))
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
