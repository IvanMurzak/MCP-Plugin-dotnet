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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A tool body that throws, end to end: a REAL <see cref="RunTool"/> builds the response, a real SignalR plugin
    /// sends it, and the server answers. Failing on the request's content (Unity's script-execute throws
    /// <c>Exception("Compilation failed...")</c> for the agent's invalid code) is HTTP 422 on the direct endpoint, a
    /// plugin fault stays 500, and the MCP channel shows the tool's own message as an ordinary error result.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ToolBodyFailureHttpStatusTests
    {
        const string CompileError = "Compilation failed: CS1002 ; expected";
        const string ToolMessage = "Tool execution failed for 'Execute script': " + CompileError;

        [Theory]
        [InlineData("8.15.0")]
        [InlineData("8.14.0")]
        [InlineData(null)] // a plugin before 8.14 skips the call guard: same answer
        public async Task ToolBodyRejectingTheRequest_Is422WithTheToolsMessage(string? pluginVersion)
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: RealTool(nameof(Tools.CompileFails)));
            editor.McpPluginVersion = pluginVersion;
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(422);
            body!["error"]!.GetValue<string>().ShouldBe(ToolMessage);

            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeTrue();
            text.ShouldBe(ToolMessage);
        }

        [Fact]
        public async Task ToolBodyFault_StaysInternalServerError()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: RealTool(nameof(Tools.Bug)));
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(500);
            body!["error"]!.GetValue<string>().ShouldBe("Tool execution failed for 'Execute script': bug");

            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeTrue();
            text.ShouldBe("Tool execution failed for 'Execute script': bug");
        }

        // An old plugin sends Internal for every throwing tool: the server does not guess from the text.
        [Fact]
        public async Task OldPluginsInternalFailure_StaysInternalServerError()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: (_, request) => Task.FromResult(
                ResponseCallTool.Error(ToolMessage, ResponseErrorKind.Internal).Pack(request.RequestID)));
            editor.McpPluginVersion = null;
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(500);
            body!["error"]!.GetValue<string>().ShouldBe(ToolMessage);
        }

        /// <summary>Answers every call by running <paramref name="method"/> through a real <see cref="McpToolManager"/>.</summary>
        static Func<FakeEnginePlugin, RequestCallTool, Task<ResponseData<ResponseCallTool>>> RealTool(string method)
        {
            var tools = new ToolRunnerCollection(new Reflector(), NullLogger.Instance);
            tools["echo"] = RunTool.CreateFromStaticMethod(new Reflector(), NullLogger.Instance, "echo",
                typeof(Tools).GetMethod(method)!, title: "Execute script");
            var manager = new McpToolManager(NullLogger<McpToolManager>.Instance, new Reflector(), tools);
            return (_, request) => manager.RunCallTool(request);
        }

        public static class Tools
        {
            public static string CompileFails() => throw new Exception(CompileError);

            public static string Bug() => throw new NullReferenceException("bug");
        }
    }
}
