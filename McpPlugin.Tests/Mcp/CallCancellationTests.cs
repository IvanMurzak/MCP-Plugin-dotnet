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
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// The server's <c>CancelCall</c> stops a running tool or system tool call: the call's token (passed to the tool
    /// and set as <see cref="ToolCallCancellation.Token"/>) fires.
    /// </summary>
    [Collection("McpPlugin")]
    public class CallCancellationTests
    {
        static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        static RequestCallTool Call(string requestId, string name = "tool") => new RequestCallTool(requestId, name, new Dictionary<string, JsonElement>());

        [Fact]
        public async Task CancelCall_StopsARunningTool_ThroughItsTokenAndTheAmbientToken()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken ambient = default;
            var tool = new ProxyTool("slow", null, "Waits until cancelled.", null, null, null, null, null, null, null, null,
                async (requestId, arguments, cancellationToken) =>
                {
                    ambient = ToolCallCancellation.Token;
                    started.SetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return ResponseCallTool.Success("not cancelled");
                });
            var plugin = new McpPluginBuilder(new Version()).AddTool("slow", tool).Build(new Reflector());
            var manager = (McpManager)plugin.McpManager;
            var hub = manager.ToolHub!;

            var call = hub.RunCallTool(Call("request-1", "slow"));
            await started.Task.WaitAsync(Wait);
            ambient.CanBeCanceled.ShouldBeTrue("the tool sees the call's token as ToolCallCancellation.Token");
            ToolCallCancellation.Token.CanBeCanceled.ShouldBeFalse("the token never leaks into the caller's flow");

            ((IClientCallCancellation)hub).CancelCall(new RequestCancelCall("request-1"));

            var answer = await call.WaitAsync(Wait);
            ambient.IsCancellationRequested.ShouldBeTrue();
            answer.Status.ShouldBe(ResponseStatus.Error, "a cancelled call answers an error nobody waits for");
            manager.CallCancellations.Count.ShouldBe(0);
        }

        [Fact]
        public async Task CancelCall_StopsARunningSystemTool_AndIgnoresOtherIds()
        {
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var systemTools = new Mock<ISystemToolManager>();
            systemTools.Setup(t => t.RunSystemTool(It.IsAny<RequestCallTool>(), It.IsAny<CancellationToken>()))
                .Returns(async (RequestCallTool request, CancellationToken _) =>
                {
                    var token = ToolCallCancellation.Token;
                    started.SetResult(token);
                    await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { });
                    return ResponseData<ResponseCallTool>.Error(request.RequestID, "cancelled", ResponseErrorKind.Internal);
                });
            var manager = new McpManager(NullLogger<McpManager>.Instance, new Reflector(), systemTools: systemTools.Object);
            var cancellation = (IClientCallCancellation)manager.SystemToolHub!;

            var call = manager.SystemToolHub!.RunSystemTool(Call("sys-1"));
            var token = await started.Task.WaitAsync(Wait);

            cancellation.CancelCall(new RequestCancelCall("another-id"));
            cancellation.CancelCall(new RequestCancelCall(""));
            await Task.Delay(200);
            token.IsCancellationRequested.ShouldBeFalse();

            cancellation.CancelCall(new RequestCancelCall("sys-1"));
            await call.WaitAsync(Wait);
            token.IsCancellationRequested.ShouldBeTrue();
            manager.CallCancellations.Count.ShouldBe(0);

            cancellation.CancelCall(new RequestCancelCall("sys-1")); // finished: ignored
        }

        /// <summary>
        /// A synchronous tool that blocks (as one waiting for an engine's main thread does) must not hold the hub
        /// caller's thread: the SignalR client processes the CancelCall only once the call's handler returns a task.
        /// </summary>
        [Fact]
        public async Task BlockingSyncTool_DoesNotHoldTheCaller_AndIsCancelled()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tool = new ProxyTool("blocking", null, "Blocks until cancelled.", null, null, null, null, null, null, null, null,
                (requestId, arguments, cancellationToken) =>
                {
                    started.SetResult(true);
                    var cancelled = ToolCallCancellation.Token.WaitHandle.WaitOne(Wait);
                    return Task.FromResult(ResponseCallTool.Success(cancelled ? "cancelled" : "not cancelled"));
                });
            var manager = (McpManager)new McpPluginBuilder(new Version()).AddTool("blocking", tool).Build(new Reflector()).McpManager;

            var stopwatch = Stopwatch.StartNew();
            var call = manager.ToolHub!.RunCallTool(Call("request-1", "blocking"));
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), "the call returned its task without running the tool inline");
            await started.Task.WaitAsync(Wait);

            ((IClientCallCancellation)manager.ToolHub).CancelCall(new RequestCancelCall("request-1"));

            var answer = await call.WaitAsync(Wait);
            answer.Value!.GetMessage().ShouldBe("cancelled");
        }

        [Fact]
        public async Task ProcessingAnswer_StaysCancellableUntilCompleted()
        {
            var registry = new CallCancellationRegistry();
            CancellationToken captured = default;

            var answer = await registry.Run("p-1", default, () =>
            {
                captured = ToolCallCancellation.Token;
                return Task.FromResult(ResponseData<ResponseCallTool>.Processing("p-1"));
            });

            answer.Status.ShouldBe(ResponseStatus.Processing);
            registry.Count.ShouldBe(1, "the tool keeps running after its Processing answer");
            var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            captured.Register(() => fired.TrySetResult(true));
            registry.Cancel("p-1");
            (await fired.Task.WaitAsync(Wait)).ShouldBeTrue();

            registry.Complete("p-1");
            registry.Count.ShouldBe(0);
        }

        [Fact]
        public async Task ProcessingAnswer_IsForgottenAfterTheRetention()
        {
            long now = 1;
            var registry = new CallCancellationRegistry(clock: () => now);
            await registry.Run("p-1", default, () => Task.FromResult(ResponseData<ResponseCallTool>.Processing("p-1")));
            registry.Count.ShouldBe(1);

            now += (long)(CallCancellationRegistry.ProcessingRetention.TotalSeconds * Stopwatch.Frequency) + 1;
            await registry.Run("p-2", default, () => Task.FromResult(ResponseData<ResponseCallTool>.Success("p-2")));

            registry.Count.ShouldBe(0);
        }

        [Fact]
        public async Task CallWithoutRequestId_RunsWithTheCallerToken()
        {
            var registry = new CallCancellationRegistry();
            using var caller = new CancellationTokenSource();
            CancellationToken seen = default;

            await registry.Run(null, caller.Token, () =>
            {
                seen = ToolCallCancellation.Token;
                return Task.FromResult(ResponseData<ResponseCallTool>.Success(string.Empty));
            });

            seen.ShouldBe(caller.Token);
            registry.Count.ShouldBe(0);
        }
    }
}
