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
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A tool call is sent to the plugin at most once: re-sending a request the plugin may already be
    /// running makes it run twice. Only the "no plugin connected yet" wait retries. Catalog calls
    /// (idempotent) keep the retry ladder.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ClientUtilsToolCallDeliveryTests
    {
        [Fact]
        public async Task ToolCall_NoAnswerWithinTimeout_IsSentOnce_AndFailsWithTimeout()
        {
            var connectionId = NewConnectionId();
            var never = new TaskCompletionSource<ResponseData<ResponseCallTool>>();
            var client = ClientReturning(never.Task);
            var (hub, strategy) = Route(client.Object, connectionId);

            var result = await InvokeToolOnce(hub, strategy, timeoutMs: 200).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Error);
            result.ErrorKind.ShouldBe(ResponseErrorKind.Timeout);
            result.Message.ShouldNotBeNull().ShouldContain("200 ms");
            VerifySent(client, Times.Once());
        }

        [Fact]
        public async Task ToolCall_ConnectionLostAfterSend_IsSentOnce_AndFailsAsUnavailable()
        {
            var connectionId = NewConnectionId();
            var client = ClientReturning(Task.FromException<ResponseData<ResponseCallTool>>(
                new IOException($"Connection '{connectionId}' disconnected.")));
            var (hub, strategy) = Route(client.Object, connectionId);

            var result = await InvokeToolOnce(hub, strategy, timeoutMs: 10_000).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Error);
            result.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            VerifySent(client, Times.Once());
        }

        [Fact]
        public async Task ToolCall_NoPluginConnectedYet_WaitsAndSendsOnceItConnects()
        {
            var connectionId = NewConnectionId();
            var client = ClientReturning(Task.FromResult(Success()));
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.SetupSequence(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>()))
                .Returns((string?)null) // a Unity domain reload: the plugin is reconnecting
                .Returns(connectionId);
            var (hub, _) = Route(client.Object, connectionId);

            var result = await InvokeToolOnce(hub, strategy.Object, timeoutMs: 10_000).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Success);
            VerifySent(client, Times.Once());
        }

        [Fact]
        public async Task ToolCall_ConnectionGoneBeforeSend_IsRetried()
        {
            // SignalR refuses an unknown connection id before writing anything, so nothing reached a plugin.
            var connectionId = NewConnectionId();
            var client = new Mock<ISingleClientProxy>();
            client.SetupSequence(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                    It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException($"Connection '{connectionId}' does not exist."))
                .ReturnsAsync(Success());
            var (hub, strategy) = Route(client.Object, connectionId);

            var result = await InvokeToolOnce(hub, strategy, timeoutMs: 10_000).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Success);
            VerifySent(client, Times.Exactly(2));
        }

        [Fact]
        public async Task RealSignalR_UnknownConnection_FailsWithTheErrorTreatedAsNotDelivered()
        {
            // Pins the exception ClientUtils.IsNotDelivered matches to what SignalR really throws, so a
            // reworded message reds here instead of silently disabling the retry-before-send.
            await using var host = await NoneAuthMcpHost.StartAsync();
            var hub = host.Services.GetRequiredService<IHubContext<McpServerHub>>();

            var ex = await Should.ThrowAsync<IOException>(() => hub.Clients.Client(NewConnectionId())
                .InvokeAsync<ResponseData<ResponseCallTool>>("RunCallTool", new RequestCallTool(), CancellationToken.None));

            ex.Message.ShouldContain("does not exist");
        }

        [Fact]
        public async Task CatalogCall_ErrorThenAnswer_IsStillRetried()
        {
            var connectionId = NewConnectionId();
            var answer = ResponseData<ResponseListTool[]>.Success("request");
            answer.Value = Array.Empty<ResponseListTool>();
            var client = new Mock<ISingleClientProxy>();
            client.SetupSequence(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(
                    It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException($"Connection '{connectionId}' disconnected."))
                .ReturnsAsync(answer);
            var (hub, strategy) = Route(client.Object, connectionId);

            var result = await ClientUtils.InvokeAsync<RequestListTool, ResponseListTool[], McpServerHub>(
                    NullLogger.Instance, hub, "RunListTool", new RequestListTool(),
                    Mock.Of<IDataArguments>(d => d.PluginTimeoutMs == 10_000), strategy)
                .WaitAsync(TimeSpan.FromSeconds(10));

            result.ShouldBeSameAs(answer);
            client.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task RemoteToolRunner_ToolSlowerThanPluginTimeout_IsSentOnce_AndAnswers()
        {
            var connectionId = NewConnectionId();
            var client = ClientReturning(DelayedSuccess(TimeSpan.FromMilliseconds(600)));
            var (hub, strategy) = Route(client.Object, connectionId);
            var arguments = new Mock<IDataArguments>();
            arguments.Setup(a => a.PluginTimeoutMs).Returns(100);
            arguments.As<IToolTimeoutArguments>().Setup(a => a.ToolTimeoutMs).Returns(10_000);
            using var runner = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, hub, arguments.Object,
                new RequestTrackingService(NullLogger<RequestTrackingService>.Instance), strategy);
            using var request = new RequestCallTool("slow", new Dictionary<string, JsonElement>());

            var result = await runner.RunCallTool(request).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Success);
            VerifySent(client, Times.Once());
        }

        [Fact]
        public async Task RemoteToolRunner_CallerCancellation_StopsTheWait()
        {
            var connectionId = NewConnectionId();
            var client = ClientReturning(new TaskCompletionSource<ResponseData<ResponseCallTool>>().Task);
            var (hub, strategy) = Route(client.Object, connectionId);
            using var runner = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, hub,
                Mock.Of<IDataArguments>(d => d.PluginTimeoutMs == 100),
                new RequestTrackingService(NullLogger<RequestTrackingService>.Instance), strategy);
            using var request = new RequestCallTool("slow", new Dictionary<string, JsonElement>());
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Should.ThrowAsync<OperationCanceledException>(() =>
                runner.RunCallTool(request, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            VerifySent(client, Times.Once());
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static string NewConnectionId() => "tool-call-delivery-" + Guid.NewGuid().ToString("N");

        static ResponseData<ResponseCallTool> Success()
            => ResponseCallTool.Success("done").Pack("request-1");

        static async Task<ResponseData<ResponseCallTool>> DelayedSuccess(TimeSpan delay)
        {
            await Task.Delay(delay);
            return Success();
        }

        static Task<ResponseData<ResponseCallTool>> InvokeToolOnce(IHubContext<McpServerHub> hub, IMcpConnectionStrategy strategy, int timeoutMs)
            => ClientUtils.InvokeOnceAsync<RequestCallTool, ResponseCallTool, McpServerHub>(
                NullLogger.Instance, hub, "RunCallTool",
                new RequestCallTool("tool", new Dictionary<string, JsonElement>()), timeoutMs, strategy);

        static Mock<ISingleClientProxy> ClientReturning(Task<ResponseData<ResponseCallTool>> answer)
        {
            var client = new Mock<ISingleClientProxy>();
            client.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                    It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(answer);
            return client;
        }

        static void VerifySent(Mock<ISingleClientProxy> client, Times times)
            => client.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);

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
