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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.ServerLaunch;
using Shouldly;
using Xunit;
using static com.IvanMurzak.McpPlugin.Common.Consts.MCP.Server;

namespace com.IvanMurzak.McpPlugin.ServerLaunch.Tests
{
    /// <summary>
    /// The shared server launch-arg builder (mcp-authorize g6 consolidation): one canonical emitter of
    /// the <c>auth=&lt;mode&gt;</c> / <c>token</c> / <c>auth-issuer</c> / <c>public-url</c> argument shape
    /// so Unity, Godot, and Unreal's sidecar never re-derive it divergently.
    /// </summary>
    public class ServerLaunchArgumentsTests
    {
        [Fact]
        public void Build_None_EmitsBaseArgs_NoCredentials()
        {
            var args = ServerLaunchArguments.Build(
                port: 8080, pluginTimeoutMs: 30000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.none);

            args.ShouldBe(new[]
            {
                $"{Args.Port}=8080",
                $"{Args.PluginTimeout}=30000",
                $"{Args.ClientTransportMethod}={Consts.MCP.Server.TransportMethod.streamableHttp}",
                $"{Args.Auth}={Consts.MCP.Server.AuthOption.none}"
            });
        }

        [Fact]
        public void Build_Token_EmitsTokenArg()
        {
            var args = ServerLaunchArguments.Build(
                port: 9000, pluginTimeoutMs: 10000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.token,
                token: "the-secret");

            args.ShouldContain($"{Args.Auth}={Consts.MCP.Server.AuthOption.token}");
            args.ShouldContain($"{Args.Token}=the-secret");
            // Token mode never emits the OAuth resource-server args.
            args.ShouldNotContain($"{Args.AuthIssuer}=");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Build_Token_WithoutSecret_Throws(string? token)
        {
            Should.Throw<ArgumentException>(() => ServerLaunchArguments.Build(
                port: 9000, pluginTimeoutMs: 10000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.token,
                token: token));
        }

        [Fact]
        public void Build_OAuth_EmitsIssuerAndPublicUrl_NoToken()
        {
            var args = ServerLaunchArguments.Build(
                port: 7000, pluginTimeoutMs: 20000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.oauth,
                authIssuer: "https://ai-game.dev",
                publicUrl: "http://localhost:7000/mcp/p/abcd1234");

            args.ShouldContain($"{Args.Auth}={Consts.MCP.Server.AuthOption.oauth}");
            args.ShouldContain($"{Args.AuthIssuer}=https://ai-game.dev");
            args.ShouldContain($"{Args.PublicUrl}=http://localhost:7000/mcp/p/abcd1234");
            // OAuth mode carries no static token.
            args.ShouldNotContain($"{Args.Token}=");
        }

        [Theory]
        [InlineData(null, "http://localhost:7000")]
        [InlineData("https://ai-game.dev", null)]
        [InlineData("", "http://localhost:7000")]
        [InlineData("https://ai-game.dev", "")]
        public void Build_OAuth_MissingIssuerOrPublicUrl_Throws(string? issuer, string? publicUrl)
        {
            Should.Throw<ArgumentException>(() => ServerLaunchArguments.Build(
                port: 7000, pluginTimeoutMs: 20000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.oauth,
                authIssuer: issuer,
                publicUrl: publicUrl));
        }

        [Fact]
        public void Build_Required_Throws_NewCallersUseToken()
        {
            // The NEW builder emits the target-state key only; the deprecated `required` alias is a
            // server-side back-compat concern, not something a fresh launch should produce.
            Should.Throw<ArgumentException>(() => ServerLaunchArguments.Build(
                port: 9000, pluginTimeoutMs: 10000,
                clientTransport: Consts.MCP.Server.TransportMethod.streamableHttp,
                authOption: Consts.MCP.Server.AuthOption.required,
                token: "x"));
        }

        [Fact]
        public void BuildCommandLine_SpaceJoinsArgs()
        {
            var line = ServerLaunchArguments.BuildCommandLine(
                port: 8080, pluginTimeoutMs: 30000,
                clientTransport: Consts.MCP.Server.TransportMethod.stdio,
                authOption: Consts.MCP.Server.AuthOption.none);

            line.ShouldBe($"{Args.Port}=8080 {Args.PluginTimeout}=30000 {Args.ClientTransportMethod}={Consts.MCP.Server.TransportMethod.stdio} {Args.Auth}={Consts.MCP.Server.AuthOption.none}");
        }
    }
}
