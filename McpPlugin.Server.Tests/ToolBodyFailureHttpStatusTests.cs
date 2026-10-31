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

        const string UnavailableMessage = "Tool execution failed for 'Execute script': " + Tools.Compiling;

        // ToolUnavailableException without notExecuted: 503 with the tool's message, and the call is never sent again
        // (the tool may have done part of its work).
        [Theory]
        [InlineData("8.16.0")]
        [InlineData("8.14.0")]
        [InlineData(null)]
        public async Task ToolUnavailable_Is503WithTheToolsMessage_AndIsNotResent(string? pluginVersion)
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: RealTool(nameof(Tools.Unavailable)));
            editor.McpPluginVersion = pluginVersion;
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(503);
            body!["error"]!.GetValue<string>().ShouldBe(UnavailableMessage);
            editor.Calls.Count.ShouldBe(1);

            var (isError, text) = await client.CallAsync("echo", new { });
            isError.ShouldBeTrue();
            text.ShouldBe(UnavailableMessage);
            editor.Calls.Count.ShouldBe(2);
        }

        // ToolUnavailableException(notExecuted: true) from an 8.14+ plugin: the call guard sends it again while the
        // reconnect window is open, then reports the refusal (with the tool's message) as 503.
        [Fact]
        public async Task ToolNotExecuted_IsResentByTheCallGuard_ThenIs503()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(null, "tool-timeout=3000", "plugin-reconnect-wait-seconds=60");
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: RealTool(nameof(Tools.NotExecuted)));
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}").WaitAsync(TimeSpan.FromSeconds(30));
            status.ShouldBe(503);
            body!["error"]!.GetValue<string>().ShouldContain(UnavailableMessage);
            editor.Calls.Count.ShouldBeGreaterThan(1, "re-sent while the window was open");
        }

        // A plugin before 8.14 skips the call guard: the not-executed refusal comes back as sent, once.
        [Fact]
        public async Task ToolNotExecuted_FromAPluginWithoutTheCallGuard_Is503AndSentOnce()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var client = new ToolCallClient(host);
            await using var editor = client.Plugin("editor", "Unity 6", onCall: RealTool(nameof(Tools.NotExecuted)));
            editor.McpPluginVersion = null;
            await editor.StartAsync();

            var (status, body) = await client.RestAsync("echo", "{}");
            status.ShouldBe(503);
            body!["error"]!.GetValue<string>().ShouldBe(UnavailableMessage);
            editor.Calls.Count.ShouldBe(1);
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

            public const string Compiling = "Scripts are compiling; retry in a few seconds.";

            public static string Unavailable() => throw new ToolUnavailableException(Compiling);

            public static string NotExecuted() => throw new ToolUnavailableException(Compiling, notExecuted: true);
        }
    }
}
