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
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public sealed class RequestTrackingCancellationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CallerCancellation_IsNotReportedAsTimeoutOrExecutionError(bool processing)
        {
            using var cts = new CancellationTokenSource();
            var initial = new TaskCompletionSource<ResponseCallTool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logger = new Mock<ILogger<RequestTrackingService>>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            using var tracker = new RequestTrackingService(logger.Object);
            var call = tracker.TrackRequestAsync("request", () => processing
                ? Task.FromResult(ResponseCallTool.Processing()) : initial.Task, TimeSpan.FromMinutes(15), cts.Token);

            cts.Cancel();
            if (!processing)
                initial.SetCanceled(cts.Token);

            await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
            var logs = logger.Invocations.Where(i => i.Method.Name == "Log").ToArray();
            logs.ShouldContain(i => (LogLevel)i.Arguments[0] == LogLevel.Debug && i.Arguments[2].ToString()!.Contains("cancelled"));
            logs.ShouldNotContain(i => (LogLevel)i.Arguments[0] >= LogLevel.Warning);
        }

        [Fact]
        public async Task ProcessingTimeout_StillReturnsATimeoutError()
        {
            using var tracker = new RequestTrackingService(new Mock<ILogger<RequestTrackingService>>().Object);

            var result = await tracker.TrackRequestAsync("request", () => Task.FromResult(ResponseCallTool.Processing()),
                TimeSpan.FromMilliseconds(1)).WaitAsync(TimeSpan.FromSeconds(5));

            result.ErrorKind.ShouldBe(ResponseErrorKind.Timeout);
            result.Status.ShouldBe(ResponseStatus.Error);
            result.GetMessage().ShouldNotBeNull().ShouldContain("may still be running");
        }

        [Fact]
        public async Task DeferredDeadline_StartsWhenProcessingArrives_NotWhenTheCallWasSent()
        {
            using var tracker = new RequestTrackingService(new Mock<ILogger<RequestTrackingService>>().Object);

            // The plugin takes longer than the timeout to say Processing, then completes right away.
            var result = await tracker.TrackRequestAsync("request", async () =>
            {
                await Task.Delay(300);
                tracker.CompleteRequest(ResponseCallTool.Success("done").SetRequestID("request"));
                return ResponseCallTool.Processing();
            }, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5));

            result.Status.ShouldBe(ResponseStatus.Success, "the deferred completion gets the full timeout from Processing on");
        }
    }
}
