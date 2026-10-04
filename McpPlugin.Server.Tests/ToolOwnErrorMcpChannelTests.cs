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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// A tool's own error answers the direct REST channel with 400, but on the MCP channel it must
    /// stay an ordinary <c>tools/call</c> result with <c>isError: true</c> and the tool's message —
    /// never a JSON-RPC error. Remote tool errors reach the MCP client through
    /// <c>ToolRouter.Call</c>, not <c>ToCallToolResult</c>, so this drives a real MCP session.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class ToolOwnErrorMcpChannelTests
    {
        [Theory]
        [InlineData(ResponseErrorKind.BadRequest)]
        [InlineData(ResponseErrorKind.Internal)]
        public async Task ToolsCall_RemoteToolError_IsErrorResultWithTheToolsMessage(ResponseErrorKind errorKind)
        {
            var hub = new ErrorToolHub(ResponseCallTool.Error("No Renderers found", errorKind).Pack("request-1"));
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var sessionId = await host.HandshakeAsync(client, "tool-own-error-mcp-channel");

            var (body, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "test", arguments = new { } }
            });

            using var doc = JsonDocument.Parse(NoneAuthMcpHost.Unwrap(body));
            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse("a tool's own error must not become a JSON-RPC error: " + body);
            var result = doc.RootElement.GetProperty("result");
            result.GetProperty("isError").GetBoolean().ShouldBeTrue();
            result.GetProperty("content").EnumerateArray().ShouldHaveSingleItem()
                .GetProperty("text").GetString().ShouldBe("No Renderers found");
            hub.Calls.ShouldBe(1, "tools/call must have reached the plugin seam");
        }

        sealed class ErrorToolHub : IClientToolHub
        {
            readonly ResponseData<ResponseCallTool> _response;
            int _calls;

            public ErrorToolHub(ResponseData<ResponseCallTool> response) => _response = response;

            public int Calls => Volatile.Read(ref _calls);

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(Array.Empty<ResponseListTool>().Pack(request.RequestID));

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
            {
                Interlocked.Increment(ref _calls);
                return Task.FromResult(_response);
            }
        }
    }
}
