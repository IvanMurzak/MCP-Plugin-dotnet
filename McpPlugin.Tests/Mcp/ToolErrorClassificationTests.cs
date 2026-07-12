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

        [Fact]
        public async Task RunCallTool_ReflectedToolExecutionFailure_IsInternal()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(new Version());
            var method = typeof(ReflectedThrowingTool).GetMethod(nameof(ReflectedThrowingTool.Throw));
            builder.WithTool("reflected-throw", "Reflected Throw", typeof(ReflectedThrowingTool), method!);
            var plugin = builder.Build(reflector);

            var response = await plugin.McpManager.ToolManager!.RunCallTool(
                new RequestCallTool("request-1", "reflected-throw", EmptyArgs));

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
        }

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
            public bool Enabled { get; set; } = true;
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
        }
    }
}
