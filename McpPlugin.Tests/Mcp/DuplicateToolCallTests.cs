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
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// The plugin runs a tool call once per request id, even if a server sends it again (older
    /// servers re-sent a tool call that was slower than <c>plugin-timeout</c>).
    /// </summary>
    [Collection("McpPlugin")]
    public class DuplicateToolCallTests
    {
        static RequestCallTool Call(string requestId) => new RequestCallTool(requestId, "tool", new Dictionary<string, JsonElement>());

        [Fact]
        public async Task SameRequestId_WhileRunningAndAfterwards_RunsTheToolOnce()
        {
            var runs = 0;
            var gate = new TaskCompletionSource<ResponseData<ResponseCallTool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new Mock<IToolManager>();
            tools.Setup(t => t.RunCallTool(It.IsAny<RequestCallTool>()))
                .Returns(() => { Interlocked.Increment(ref runs); return gate.Task; });
            var manager = new McpManager(NullLogger<McpManager>.Instance, new Reflector(), tools.Object);

            var first = manager.ToolHub!.RunCallTool(Call("request-1"));
            var resent = manager.ToolHub.RunCallTool(Call("request-1"));
            var answer = ResponseData<ResponseCallTool>.Success("request-1");
            gate.SetResult(answer);

            (await first).ShouldBeSameAs(answer);
            (await resent).ShouldBeSameAs(answer);
            (await manager.ToolHub.RunCallTool(Call("request-1"))).ShouldBeSameAs(answer);
            runs.ShouldBe(1);
            manager.RequestActivity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public async Task DifferentRequestIds_EachRun()
        {
            var runs = 0;
            var tools = new Mock<IToolManager>();
            var systemTools = new Mock<ISystemToolManager>();
            tools.Setup(t => t.RunCallTool(It.IsAny<RequestCallTool>()))
                .Returns(() => { Interlocked.Increment(ref runs); return Task.FromResult(ResponseData<ResponseCallTool>.Success("id")); });
            systemTools.Setup(t => t.RunSystemTool(It.IsAny<RequestCallTool>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Interlocked.Increment(ref runs); return Task.FromResult(ResponseData<ResponseCallTool>.Success("id")); });
            var manager = new McpManager(NullLogger<McpManager>.Instance, new Reflector(), tools.Object, systemTools: systemTools.Object);

            await manager.ToolHub!.RunCallTool(Call("a"));
            await manager.ToolHub.RunCallTool(Call("b"));
            await manager.SystemToolHub!.RunSystemTool(Call("a"));
            await manager.SystemToolHub.RunSystemTool(Call("a"));

            runs.ShouldBe(3, "tool and system tool calls are tracked apart; only the repeated system call is skipped");
        }

        [Fact]
        public async Task Guard_EmptyRequestId_AlwaysRuns_AndExpiredEntries_RunAgain()
        {
            long now = 1;
            var guard = new DuplicateRequestGuard<int>(retention: TimeSpan.FromSeconds(10), clock: () => now);
            var runs = 0;
            Task<int> Run() => Task.FromResult(Interlocked.Increment(ref runs));

            await guard.Run("", Run);
            await guard.Run(null, Run);
            runs.ShouldBe(2);

            (await guard.Run("x", Run)).ShouldBe(3);
            now += 9 * Stopwatch.Frequency;
            (await guard.Run("x", Run)).ShouldBe(3, "inside the retention window the first result is reused");
            now += 4 * Stopwatch.Frequency; // past retention and past the quarter-window sweep throttle
            (await guard.Run("x", Run)).ShouldBe(4, "an entry older than the retention window is forgotten");
        }

        [Fact]
        public async Task Guard_KeepsAtMostTheCap_OfFinishedEntries_AndNeverEvictsRunningOnes()
        {
            long now = 1;
            var guard = new DuplicateRequestGuard<int>(maxCompletedEntries: 2, clock: () => now);
            var runs = 0;
            Task<int> Run() => Task.FromResult(Interlocked.Increment(ref runs));
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var running = guard.Run("running", () => gate.Task);
            foreach (var id in new[] { "a", "b", "c" })
            {
                now++;
                await guard.Run(id, Run);
            }
            runs.ShouldBe(3);

            (await guard.Run("c", Run)).ShouldBe(3, "the newest finished entries are kept");
            (await guard.Run("b", Run)).ShouldBe(2);
            (await guard.Run("a", Run)).ShouldBe(4, "the oldest finished entry was evicted past the cap");
            guard.Run("running", Run).ShouldBeSameAs(running, "a running entry is never evicted");

            gate.SetResult(7);
            (await running).ShouldBe(7);
        }

        [Fact]
        public async Task Guard_CancelledRun_IsForgotten_SoALaterCopyRuns()
        {
            var guard = new DuplicateRequestGuard<int>();

            await Should.ThrowAsync<OperationCanceledException>(
                () => guard.Run("x", () => Task.FromCanceled<int>(new CancellationToken(canceled: true))));

            (await guard.Run("x", () => Task.FromResult(5))).ShouldBe(5);
        }
    }
}
