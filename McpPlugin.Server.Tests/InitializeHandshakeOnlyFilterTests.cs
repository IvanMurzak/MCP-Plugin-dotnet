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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Transport;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class InitializeHandshakeOnlyFilterTests
    {
        static JsonRpcRequest Request(string method, string? metaVersion = null)
        {
            var parameters = new JsonObject();
            if (metaVersion != null)
                parameters["_meta"] = new JsonObject { [MetaKeys.ProtocolVersion] = metaVersion };
            return new JsonRpcRequest { Id = new RequestId(1), Method = method, Params = parameters };
        }

        /// <summary>Runs the filter; returns whether it passed the message on to the next handler.</summary>
        static async Task<bool> RunAsync(JsonRpcRequest request)
        {
            var passed = false;
            var handler = InitializeHandshakeOnlyFilter.Apply((_, _) => { passed = true; return Task.CompletedTask; });
            await handler(new MessageContext(new Mock<McpServer>().Object, request), CancellationToken.None);
            return passed;
        }

        [Fact]
        public async Task ServerDiscover_IsRefusedWithUnsupportedProtocolVersion_AndTheInitializeEraList()
        {
            var ex = await Should.ThrowAsync<UnsupportedProtocolVersionException>(
                () => RunAsync(Request(RequestMethods.ServerDiscover, metaVersion: "2026-07-28")));

            ex.ErrorCode.ShouldBe(McpErrorCode.UnsupportedProtocolVersion);
            ex.Requested.ShouldBe("2026-07-28");
            ex.Supported.ShouldBe(InitializeHandshakeOnlyFilter.SupportedProtocolVersions);
            ex.Supported.ShouldNotContain("2026-07-28");
        }

        [Fact]
        public async Task OtherRequests_PassThrough_EvenWhenDeclaringAModernVersion()
        {
            // The SDK has already committed a declared 2026-07-28 to the session for these; refusing would strand it.
            (await RunAsync(Request(RequestMethods.ToolsList, metaVersion: "2026-07-28"))).ShouldBeTrue();
            (await RunAsync(Request(RequestMethods.Initialize))).ShouldBeTrue();
            (await RunAsync(Request(RequestMethods.ToolsCall))).ShouldBeTrue();
        }
    }
}
