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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ToolErrorClassificationTests
    {
        [Fact]
        public async Task RunCallTool_NullRequest_IsBadRequest()
        {
            var manager = ToolManager();

            var response = await manager.RunCallTool(null!);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
        }

        [Fact]
        public async Task RunCallTool_MissingName_IsBadRequest()
        {
            var manager = ToolManager();

            var response = await manager.RunCallTool(new RequestCallTool("request-1", "", EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
        }

        [Fact]
        public async Task RunCallTool_UnknownTool_IsNotFound()
        {
            var manager = ToolManager();

            var response = await manager.RunCallTool(new RequestCallTool("request-1", "missing", EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.NotFound);
        }

        [Theory]
        [MemberData(nameof(ExceptionCases))]
        public async Task RunCallTool_ClassifiesExceptions(Exception exception, ResponseErrorKind expected)
        {
            var manager = ToolManager(new ThrowingTool(exception));

            var response = await manager.RunCallTool(new RequestCallTool("request-1", ThrowingTool.ToolName, EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(expected);
        }

        // A tool that throws a plain exception failed on what the request asked for (Unity's script-execute
        // throws Exception("Compilation failed...") for the agent's invalid code): BadRequest + 422, so the
        // direct HTTP endpoint answers 422, not 500. BadRequest (not a new kind) keeps older servers working.
        [Fact]
        public async Task RunCallTool_ReflectedToolExecutionFailure_IsUnprocessable()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(new Version());
            var method = typeof(ReflectedThrowingTool).GetMethod(nameof(ReflectedThrowingTool.Throw));
            builder.WithTool("reflected-throw", "Reflected Throw", typeof(ReflectedThrowingTool), method!);
            var plugin = builder.Build(reflector);

            var response = await plugin.McpManager.ToolManager!.RunCallTool(
                new RequestCallTool("request-1", "reflected-throw", EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
            response.HttpStatusCode.ShouldBe(ResponseCallTool.UnprocessableStatusCode);
            response.Value!.HttpStatusCode.ShouldBe(422);
            response.Value.GetMessage().ShouldBe("Tool execution failed for 'Reflected Throw': reflected tool failed");
        }

        [Theory]
        [MemberData(nameof(ToolBodyExceptionCases))]
        public async Task RunTool_NamedRun_ClassifiesToolBodyExceptions(string exception, bool async, bool frameworkFault)
        {
            var tool = ToolBodyThrowingTool(async);

            var response = await tool.Run("request-1", ToolBodyArgs(exception), CancellationToken.None);

            AssertToolBodyFailure(response, exception, frameworkFault);
        }

        [Theory]
        [MemberData(nameof(ToolBodyExceptionCases))]
        public async Task RunTool_PositionalRun_ClassifiesToolBodyExceptions(string exception, bool async, bool frameworkFault)
        {
            var tool = ToolBodyThrowingTool(async);

            var response = await tool.Run("request-1", CancellationToken.None, exception);

            AssertToolBodyFailure(response, exception, frameworkFault);
        }

        public static IEnumerable<object[]> ToolBodyExceptionCases()
        {
            foreach (var async in new[] { false, true })
            {
                // the request's content: HTTP 422
                yield return new object[] { nameof(Exception), async, false };
                yield return new object[] { nameof(InvalidOperationException), async, false };
                yield return new object[] { nameof(NotSupportedException), async, false };
                yield return new object[] { nameof(ApplicationException), async, false };
                yield return new object[] { "Custom", async, false };
                // a fault in the plugin: HTTP 500
                yield return new object[] { nameof(NullReferenceException), async, true };
                yield return new object[] { nameof(IndexOutOfRangeException), async, true };
                yield return new object[] { nameof(InvalidCastException), async, true };
                yield return new object[] { nameof(NotImplementedException), async, true };
                yield return new object[] { nameof(MissingMethodException), async, true };
                yield return new object[] { nameof(TaskCanceledException), async, true };
            }
        }

        static void AssertToolBodyFailure(ResponseCallTool response, string exception, bool frameworkFault)
        {
            response.Status.ShouldBe(ResponseStatus.Error);
            response.RequestID.ShouldBe("request-1");
            response.GetMessage().ShouldBe($"Tool execution failed for 'Tool Body Throw': thrown {exception}");
            if (frameworkFault)
            {
                response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
                response.HttpStatusCode.ShouldBeNull();
            }
            else
            {
                response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
                response.HttpStatusCode.ShouldBe(422);
            }
        }

        // A tool that succeeded but returned something the plugin cannot serialize is our fault, not the request's.
        [Fact]
        public async Task RunTool_UnserializableResult_IsInternal()
        {
            var method = typeof(ReflectedThrowingTool).GetMethod(nameof(ReflectedThrowingTool.Unserializable));
            var tool = RunTool.CreateFromStaticMethod(new Reflector(), NullLogger.Instance, "unserializable", method!);

            var response = await tool.Run("request-1", EmptyArgs, CancellationToken.None);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
            response.HttpStatusCode.ShouldBeNull();
        }

        // Argument binding failures are unchanged: 400 with the binding message, no 422.
        [Fact]
        public async Task RunTool_ArgumentBindingFailure_StaysBadRequestWithout422()
        {
            var method = typeof(ReflectedThrowingTool).GetMethod(nameof(ReflectedThrowingTool.Typed));
            var tool = RunTool.CreateFromStaticMethod(new Reflector(), NullLogger.Instance, "typed", method!, title: "Typed");
            var args = new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement("not a number") };

            // 'count' cannot bind: binding fails before the tool body runs
            var response = await tool.Run("request-1", args, CancellationToken.None);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.BadRequest);
            response.HttpStatusCode.ShouldBeNull();
            response.GetMessage()!.ShouldStartWith("Parameter validation failed for tool 'Typed'");
        }

        static RunTool ToolBodyThrowingTool(bool async)
        {
            var method = typeof(ReflectedThrowingTool).GetMethod(async
                ? nameof(ReflectedThrowingTool.ThrowNamedAsync)
                : nameof(ReflectedThrowingTool.ThrowNamed));
            return RunTool.CreateFromStaticMethod(new Reflector(), NullLogger.Instance, "tool-body-throw", method!, title: "Tool Body Throw");
        }

        static IReadOnlyDictionary<string, JsonElement> ToolBodyArgs(string exception)
            => new Dictionary<string, JsonElement> { ["exception"] = JsonSerializer.SerializeToElement(exception) };

        [Fact]
        public async Task RunSystemTool_UnknownTool_IsNotFound()
        {
            var manager = SystemToolManager();

            var response = await manager.RunSystemTool(new RequestCallTool("request-1", "missing", EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.NotFound);
        }

        [Theory]
        [MemberData(nameof(ExceptionCases))]
        public async Task RunSystemTool_ClassifiesExceptions(Exception exception, ResponseErrorKind expected)
        {
            var manager = SystemToolManager(new ThrowingTool(exception));

            var response = await manager.RunSystemTool(new RequestCallTool("request-1", ThrowingTool.ToolName, EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(expected);
        }

        public static IEnumerable<object[]> ExceptionCases()
        {
            yield return new object[] { new ArgumentException("bad arg"), ResponseErrorKind.BadRequest };
            yield return new object[] { new FormatException("bad format"), ResponseErrorKind.BadRequest };
            yield return new object[] { new JsonException("bad json"), ResponseErrorKind.BadRequest };
            yield return new object[] { new FileNotFoundException("missing file"), ResponseErrorKind.NotFound };
            yield return new object[] { new DirectoryNotFoundException("missing directory"), ResponseErrorKind.NotFound };
            yield return new object[] { new KeyNotFoundException("missing key"), ResponseErrorKind.NotFound };
            yield return new object[] { new InvalidOperationException("bad state"), ResponseErrorKind.Conflict };
            yield return new object[] { new TimeoutException("too slow"), ResponseErrorKind.Timeout };
            yield return new object[] { new ApplicationException("bug"), ResponseErrorKind.Internal };
        }

        static readonly IReadOnlyDictionary<string, JsonElement> EmptyArgs = new Dictionary<string, JsonElement>();

        static McpToolManager ToolManager(IRunTool? tool = null)
        {
            var tools = new ToolRunnerCollection(new Reflector(), NullLogger.Instance);
            if (tool != null)
                tools[tool.Name] = tool;

            return new McpToolManager(NullLogger<McpToolManager>.Instance, new Reflector(), tools);
        }

        static McpSystemToolManager SystemToolManager(IRunTool? tool = null)
        {
            var tools = new SystemToolRunnerCollection(new Reflector(), NullLogger.Instance);
            if (tool != null)
                tools[tool.Name] = tool;

            return new McpSystemToolManager(NullLogger<McpSystemToolManager>.Instance, tools);
        }

        sealed class ThrowingTool : IRunTool
        {
            public const string ToolName = "throwing";
            readonly Exception _exception;

            public ThrowingTool(Exception exception)
            {
                _exception = exception;
            }

            public string Name => ToolName;
            public string? Title => "Throwing";
            public string? Description => "Throws for tests";
            public string? SkillDescription => null;
            public string? SkillBody => null;
            public JsonNode? InputSchema => JsonNode.Parse("{}");
            public JsonNode? OutputSchema => JsonNode.Parse("{}");
            public bool Visible { get; set; } = true;
            public bool? ReadOnlyHint => null;
            public bool? DestructiveHint => null;
            public bool? IdempotentHint => null;
            public bool? OpenWorldHint => null;
            public int TokenCount => 0;

            public Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default)
                => Task.FromException<ResponseCallTool>(_exception);
        }

        sealed class ReflectedThrowingTool
        {
            public string Throw() => throw new ApplicationException("reflected tool failed");

            public static string ThrowNamed(string exception) => throw Create(exception);

            public static async Task<string> ThrowNamedAsync(string exception)
            {
                await Task.Yield();
                throw Create(exception);
            }

            public static int Typed(int count) => count;

            // A delegate: System.Text.Json cannot serialize it.
            public static Action Unserializable() => () => { };

            static Exception Create(string name)
            {
                var message = "thrown " + name;
                return name switch
                {
                    nameof(Exception) => new Exception(message),
                    nameof(InvalidOperationException) => new InvalidOperationException(message),
                    nameof(NotSupportedException) => new NotSupportedException(message),
                    nameof(ApplicationException) => new ApplicationException(message),
                    "Custom" => new CustomToolException(message),
                    nameof(NullReferenceException) => new NullReferenceException(message),
                    nameof(IndexOutOfRangeException) => new IndexOutOfRangeException(message),
                    nameof(InvalidCastException) => new InvalidCastException(message),
                    nameof(NotImplementedException) => new NotImplementedException(message),
                    nameof(MissingMethodException) => new MissingMethodException(message),
                    nameof(TaskCanceledException) => new TaskCanceledException(message),
                    _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
                };
            }
        }

        sealed class CustomToolException : Exception
        {
            public CustomToolException(string message) : base(message) { }
        }
    }
}
