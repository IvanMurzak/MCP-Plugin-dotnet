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
using com.IvanMurzak.McpPlugin.Server.Transport;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    public class InitializeHandshakeOnlyFilterTests
    {
        static JsonRpcRequest Request(string method, string? metaVersion = null, string? headerVersion = null)
        {
            var parameters = new JsonObject();
            if (metaVersion != null)
                parameters["_meta"] = new JsonObject { [MetaKeys.ProtocolVersion] = metaVersion };
            var request = new JsonRpcRequest { Id = new RequestId(1), Method = method, Params = parameters };
            if (headerVersion != null)
                request.Context = new JsonRpcMessageContext { ProtocolVersion = headerVersion };
            return request;
        }

        [Fact]
        public void ServerDiscover_IsRefused_EvenWithoutDeclaredVersion()
        {
            InitializeHandshakeOnlyFilter.TryGetRefusedVersion(Request(RequestMethods.ServerDiscover), out var requested).ShouldBeTrue();
            requested.ShouldBe("2026-07-28");
        }

        [Fact]
        public void ModernMetaVersion_IsRefused()
        {
            InitializeHandshakeOnlyFilter.TryGetRefusedVersion(Request(RequestMethods.ToolsList, metaVersion: "2026-07-28"), out var requested).ShouldBeTrue();
            requested.ShouldBe("2026-07-28");
        }

        [Fact]
        public void ModernHeaderVersion_IsRefused()
        {
            InitializeHandshakeOnlyFilter.TryGetRefusedVersion(Request(RequestMethods.ToolsList, headerVersion: "2026-07-28"), out _).ShouldBeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("2025-11-25")]
        [InlineData("2025-06-18")]
        [InlineData("2024-11-05")]
        public void InitializeEraRequests_PassThrough(string? headerVersion)
        {
            InitializeHandshakeOnlyFilter.TryGetRefusedVersion(Request(RequestMethods.Initialize, headerVersion: headerVersion), out _).ShouldBeFalse();
            InitializeHandshakeOnlyFilter.TryGetRefusedVersion(Request(RequestMethods.ToolsCall, headerVersion: headerVersion), out _).ShouldBeFalse();
        }
    }
}
