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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server
{
    /// <summary>
    /// Hosted service that coordinates graceful shutdown of MCP sessions.
    /// Registered with the DI container so it stops BEFORE the MCP transport
    /// infrastructure, giving active sessions time to send clean disconnect
    /// signals to MCP clients rather than abrupt TCP resets.
    /// </summary>
    public sealed class McpGracefulShutdownService : IHostedService
    {
        readonly ILogger<McpGracefulShutdownService> _logger;
        readonly IHostApplicationLifetime _lifetime;
        readonly IMcpSessionTracker _sessionTracker;
        CancellationTokenRegistration _stoppingRegistration;

        public McpGracefulShutdownService(
            ILogger<McpGracefulShutdownService> logger,
            IHostApplicationLifetime lifetime,
            IMcpSessionTracker sessionTracker)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _stoppingRegistration = _lifetime.ApplicationStopping.Register(OnApplicationStopping);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            var remaining = _sessionTracker.ActiveSessionCount;
            if (remaining > 0)
                _logger.LogWarning("Shutdown completing with {count} MCP session(s) still tracked. " +
                    "These sessions will be terminated by the transport layer.", remaining);
            else
                _logger.LogInformation("All MCP sessions have been cleaned up. Shutdown complete.");

            _stoppingRegistration.Dispose();
            return Task.CompletedTask;
        }

        void OnApplicationStopping()
        {
            var count = _sessionTracker.ActiveSessionCount;
            if (count > 0)
            {
                _logger.LogWarning("Server is shutting down. {count} active MCP session(s) will be gracefully terminated. " +
                    "Clients should detect the clean disconnect and reconnect to the new server instance.", count);
            }
            else
            {
                _logger.LogInformation("Server is shutting down. No active MCP sessions.");
            }
        }
    }
}
