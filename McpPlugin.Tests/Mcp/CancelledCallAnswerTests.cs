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
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// DEV-138: a tool call that stops because its caller cancelled it (<see cref="ToolCallCancellation.Token"/> fired
    /// and the tool threw <see cref="OperationCanceledException"/>) answers <see cref="ResponseCallTool.Cancelled"/>
    /// and logs no error; an <see cref="OperationCanceledException"/> while the call's token has NOT fired stays a
    /// fault (HTTP 500), as before. Also covers the public <see cref="ToolCallCancellation.RunWith{T}"/>.
    /// </summary>
    [Collection("McpPlugin")]
    public class CancelledCallAnswerTests
    {
        static readonly Dictionary<string, JsonElement> EmptyArgs = new Dictionary<string, JsonElement>();

        public static class CancellableTool
        {
            public static string Run()
            {
                ToolCallCancellation.Token.ThrowIfCancellationRequested();
                return "ran";
            }

            public static async Task<string> RunAsync()
            {
                await Task.Yield();
                await Task.Delay(Timeout.Infinite, ToolCallCancellation.Token);
                return "ran";
            }

            public static string WaitAll()
            {
                Task.WaitAll(Task.Delay(Timeout.Infinite, ToolCallCancellation.Token), Task.Delay(Timeout.Infinite, ToolCallCancellation.Token));
                return "ran";
            }

            public static string ThrowOwnCancel() => throw new OperationCanceledException("the tool's own timeout");
        }

        static CancellationToken Cancelled() => new CancellationToken(canceled: true);

        static RunTool Tool(string method, ILogger logger)
            => new RunTool(new Reflector(), logger, method, typeof(CancellableTool).GetMethod(method)!);

        static void ShouldBeCancelledAnswer(ResponseCallTool response)
        {
            response.Status.ShouldBe(ResponseStatus.Error);
            ResponseCallTool.IsCancelled(response).ShouldBeTrue();
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest, "an existing kind, never Internal");
            response.HttpStatusCode.ShouldBe(ResponseCallTool.CancelledStatusCode);
        }

        [Theory]
        [InlineData(nameof(CancellableTool.Run))]
        [InlineData(nameof(CancellableTool.RunAsync))]
        [InlineData(nameof(CancellableTool.WaitAll))]
        public async Task RunTool_CancelledByItsCaller_AnswersCancelled_AndLogsNoError(string method)
        {
            var logger = new CapturingLogger();
            var tool = Tool(method, logger);

            var response = await ToolCallCancellation.RunWith(Cancelled(), () => tool.Run("request-1", EmptyArgs));

            ShouldBeCancelledAnswer(response);
            response.RequestID.ShouldBe("request-1");
            logger.AtOrAbove(LogLevel.Warning).ShouldBeEmpty();
        }

        [Fact]
        public async Task RunTool_OperationCanceled_WithoutACancelledCall_StaysAFault()
        {
            var logger = new CapturingLogger();
            var tool = Tool(nameof(CancellableTool.ThrowOwnCancel), logger);

            var response = await tool.Run("request-1", EmptyArgs);

            response.Status.ShouldBe(ResponseStatus.Error);
            ResponseCallTool.IsCancelled(response).ShouldBeFalse();
            response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
            logger.AtOrAbove(LogLevel.Error).ShouldNotBeEmpty();
        }

        [Fact]
        public async Task ToolManager_ProxyToolCancelledByItsCaller_AnswersCancelled_AndLogsNoError()
        {
            var logger = new CapturingLogger();
            var proxy = WaitingProxy("slow");
            var tools = new ToolRunnerCollection(new Reflector(), NullLogger.Instance) { [proxy.Name] = proxy };
            var manager = new McpToolManager(new Typed<McpToolManager>(logger), new Reflector(), tools);

            var response = await ToolCallCancellation.RunWith(Cancelled(),
                () => manager.RunCallTool(new RequestCallTool("request-1", "slow", EmptyArgs)));

            ShouldBeCancelledAnswer(response.Value!);
            response.HttpStatusCode.ShouldBe(ResponseCallTool.CancelledStatusCode);
            logger.AtOrAbove(LogLevel.Warning).ShouldBeEmpty();
        }

        [Fact]
        public async Task SystemToolManager_CancelledByItsCaller_AnswersCancelled_AndLogsNoError()
        {
            var logger = new CapturingLogger();
            var proxy = WaitingProxy("sys");
            var tools = new SystemToolRunnerCollection(new Reflector(), NullLogger.Instance) { [proxy.Name] = proxy };
            var manager = new McpSystemToolManager(new Typed<McpSystemToolManager>(logger), tools);

            var response = await ToolCallCancellation.RunWith(Cancelled(),
                () => manager.RunSystemTool(new RequestCallTool("request-1", "sys", EmptyArgs)));

            ShouldBeCancelledAnswer(response.Value!);
            response.HttpStatusCode.ShouldBe(ResponseCallTool.CancelledStatusCode);
            logger.AtOrAbove(LogLevel.Warning).ShouldBeEmpty();
        }

        [Fact]
        public async Task RunWith_SetsTheTokenForTheBodyAndItsContinuations_AndRestoresIt()
        {
            using var outer = new CancellationTokenSource();
            using var inner = new CancellationTokenSource();
            ToolCallCancellation.Token.CanBeCanceled.ShouldBeFalse("outside a call");

            var seen = await ToolCallCancellation.RunWith(outer.Token, async () =>
            {
                var before = ToolCallCancellation.Token;
                var nested = ToolCallCancellation.RunWith(inner.Token, () => ToolCallCancellation.Token);
                await Task.Yield();
                var afterAwait = ToolCallCancellation.Token;
                var onAnotherThread = await Task.Run(() => ToolCallCancellation.Token);
                return (before, nested, afterAwait, onAnotherThread);
            });

            seen.before.ShouldBe(outer.Token);
            seen.nested.ShouldBe(inner.Token);
            seen.afterAwait.ShouldBe(outer.Token, "a nested RunWith restores the outer token");
            seen.onAnotherThread.ShouldBe(outer.Token, "the token flows into work the body starts");
            ToolCallCancellation.Token.CanBeCanceled.ShouldBeFalse("the token never leaks into the caller's flow");

            CancellationToken fromAction = default;
            ToolCallCancellation.RunWith(inner.Token, () => { fromAction = ToolCallCancellation.Token; });
            fromAction.ShouldBe(inner.Token);
            ToolCallCancellation.Token.CanBeCanceled.ShouldBeFalse();

            Should.Throw<InvalidOperationException>(() =>
                ToolCallCancellation.RunWith<int>(inner.Token, () => throw new InvalidOperationException()));
            ToolCallCancellation.Token.CanBeCanceled.ShouldBeFalse("restored when the body throws");
        }

        /// <summary>A tool that waits for <see cref="ToolCallCancellation.Token"/>.</summary>
        static ProxyTool WaitingProxy(string name)
            => new ProxyTool(name, null, "Waits until cancelled.", null, null, null, null, null, null, null, null,
                async (requestId, arguments, cancellationToken) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return ResponseCallTool.Success("not cancelled");
                });

        sealed class Typed<T> : ILogger<T>
        {
            readonly ILogger _inner;
            public Typed(ILogger inner) => _inner = inner;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
