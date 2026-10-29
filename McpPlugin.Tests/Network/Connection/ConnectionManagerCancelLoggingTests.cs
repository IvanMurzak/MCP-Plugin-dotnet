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
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// A connect attempt canceled by our own disconnect is not a problem to warn about. Unity exits play
    /// mode right after the domain reload of a PlayMode test run, while the plugin is still connecting.
    /// </summary>
    public class ConnectionManagerCancelLoggingTests
    {
        const string Endpoint = "http://localhost:5000/hub";

        [Fact]
        public async Task DisconnectImmediate_DuringConnect_DoesNotWarn()
        {
            var logger = new CapturingLogger();
            var connectionCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IHubConnectionProvider>();
            provider
                .Setup(x => x.CreateConnectionAsync(Endpoint))
                .ReturnsAsync(() =>
                {
                    connectionCreated.TrySetResult(true);
                    return new HubConnectionBuilder()
                        .WithUrl("http://localhost:9999/test", options =>
                        {
                            options.Transports = HttpTransportType.WebSockets;
                            options.SkipNegotiation = true;
                        })
                        .Build();
                });

            await using var connectionManager = new ConnectionManager(
                logger, new Common.Version { Api = "1.0.0", Plugin = "1.0.0", Environment = "test" }, Endpoint, provider.Object);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var connectTask = Task.Run(() => connectionManager.Connect(cts.Token));
            await connectionCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Inside the 1 s stabilization delay that follows the HubConnection creation.
            connectionManager.DisconnectImmediate();

            (await connectTask.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
            logger.Warnings.ShouldNotContain(w => w.Contains("anceled"));
        }
    }
}
