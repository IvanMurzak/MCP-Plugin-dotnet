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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public sealed class ClientUtilsCancellationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CallerCancellation_StopsInvocationWithoutTimeoutOrErrorLogs(bool cancelInvocation)
        {
            using var cts = new CancellationTokenSource();
            var pending = new TaskCompletionSource<ResponseData<ResponseListTool[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Mock<ISingleClientProxy>();
            client.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cts.Cancel();
                    if (cancelInvocation)
                        pending.SetCanceled(cts.Token);
                    return pending.Task;
                });
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var (hub, strategy) = Route(client.Object, Guid.NewGuid().ToString("N"));

            await Should.ThrowAsync<OperationCanceledException>(() => Invoke(logger.Object, hub, strategy, 900_000, cts.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));

            client.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
            var logs = logger.Invocations.Where(i => i.Method.Name == "Log").ToArray();
            logs.ShouldContain(i => (LogLevel)i.Arguments[0] == LogLevel.Debug && i.Arguments[2].ToString()!.Contains("cancelled"));
            logs.ShouldNotContain(i => (LogLevel)i.Arguments[0] >= LogLevel.Warning);
        }

        [Fact]
        public async Task GenuineTimeout_IsLoggedTruthfullyAndTheConnectedClientCanStillAnswer()
        {
            var connectionId = Guid.NewGuid().ToString("N");
            var pending = new TaskCompletionSource<ResponseData<ResponseListTool[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = ResponseData<ResponseListTool[]>.Success("request");
            response.Value = Array.Empty<ResponseListTool>();
            var client = new Mock<ISingleClientProxy>();
            client.SetupSequence(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(pending.Task)
                .ReturnsAsync(response);
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var (hub, strategy) = Route(client.Object, connectionId);
            ClientUtils.AddClient<McpServerHub>(connectionId, null);
            try
            {
                var result = await Invoke(logger.Object, hub, strategy, 1).WaitAsync(TimeSpan.FromSeconds(10));

                result.ShouldBeSameAs(response);
                ClientUtils.GetAllConnectionIds(typeof(McpServerHub)).ShouldContain(connectionId);
                var logs = logger.Invocations.Where(i => i.Method.Name == "Log").Select(i => i.Arguments[2].ToString()!).ToArray();
                logs.ShouldContain(l => l.Contains("Timeout:") && l.Contains("Retrying") && l.Contains(connectionId));
                logs.ShouldNotContain(l => l.Contains("Removing from ConnectedClients"));
            }
            finally
            {
                pending.TrySetResult(response);
                ClientUtils.RemoveClient<McpServerHub>(connectionId, null);
            }
        }

        static Task<ResponseData<ResponseListTool[]>> Invoke(ILogger logger, IHubContext<McpServerHub> hub,
            IMcpConnectionStrategy strategy, int timeoutMs, CancellationToken cancellationToken = default)
            => ClientUtils.InvokeAsync<RequestListTool, ResponseListTool[], McpServerHub>(logger, hub, "RunListTool",
                new RequestListTool(), Mock.Of<IDataArguments>(d => d.PluginTimeoutMs == timeoutMs), strategy,
                cancellationToken: cancellationToken);

        static (IHubContext<McpServerHub>, IMcpConnectionStrategy) Route(ISingleClientProxy client, string connectionId)
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Client(connectionId)).Returns(client);
            var hub = new Mock<IHubContext<McpServerHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.Setup(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>())).Returns(connectionId);
            return (hub.Object, strategy.Object);
        }
    }
}
