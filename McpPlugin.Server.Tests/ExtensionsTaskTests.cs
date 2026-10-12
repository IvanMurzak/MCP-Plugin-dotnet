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
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public sealed class ExtensionsTaskTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CallerCancellation_ThrowsInsteadOfReportingTimeout(bool generic)
        {
            using var cts = new CancellationTokenSource();
            var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var wait = Wait(pending.Task, generic, TimeSpan.FromMinutes(15), cts.Token);

            cts.Cancel();

            var error = await Should.ThrowAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5)));
            error.CancellationToken.ShouldBe(cts.Token);
            pending.Task.IsCompleted.ShouldBeFalse();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AlreadyCancelledCaller_ThrowsEvenWhenTaskIsComplete(bool generic)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Should.ThrowAsync<OperationCanceledException>(() => Wait(Task.FromResult(1), generic, TimeSpan.Zero, cts.Token));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ActualTimeout_ReturnsFalseWithoutCancellingTheTask(bool generic)
        {
            var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            (await Wait(pending.Task, generic, TimeSpan.Zero)).ShouldBeFalse();
            pending.Task.IsCompleted.ShouldBeFalse();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CompletedTask_ReturnsTrueAndPreservesItsResult(bool generic)
        {
            var task = Task.FromResult(42);

            (await Wait(task, generic, TimeSpan.FromMinutes(15))).ShouldBeTrue();
            (await task).ShouldBe(42);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FaultedTask_ReturnsTrueSoTheCallerCanObserveTheFault(bool generic)
        {
            var task = Task.FromException<int>(new InvalidOperationException("plugin failure"));

            (await Wait(task, generic, TimeSpan.FromMinutes(15))).ShouldBeTrue();
            (await Should.ThrowAsync<InvalidOperationException>(() => task)).Message.ShouldBe("plugin failure");
        }

        static Task<bool> Wait(Task<int> task, bool generic, TimeSpan timeout, CancellationToken cancellationToken = default)
            => generic ? task.WaitWithTimeout(timeout, cancellationToken) : ((Task)task).WaitWithTimeout(timeout, cancellationToken);
    }
}
