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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Plugin reconnects through the real MCP pipeline (Streamable HTTP, oauth routing,
    /// <c>ToolRouter</c>, <c>ToolDispatcher</c>), and host shutdown closing plugin connections.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginReconnectOverHttpTests
    {
        const string AccountA = "acct-reconnect-a";
        const string AccountB = "acct-reconnect-b";
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";

        readonly ConcurrentDictionary<string, Mock<ISingleClientProxy>> _plugins = new ConcurrentDictionary<string, Mock<ISingleClientProxy>>();

        [Fact]
        public async Task OAuth_PluginReconnecting_KeepsItsToolsForItsAccountOnly_AndAToolCallWaitsForIt()
        {
            await using var host = await OAuthMcpHost.StartAsync(
                services => services.AddSingleton(FakeHubContext()),
                AccountA,
                "plugin-reconnect-wait-seconds=10");
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            using var http = new HttpClient();

            Plugin("conn-a1", callAnswer: "from-old-connection");
            Register(strategy, catalog, "conn-a1");
            var sessionA = await host.HandshakeAsync(http);
            (await host.CallAsync(http, sessionA, "tools/list")).ShouldContain("engine_tool");

            // The plugin drops (a recompile): account A still sees its tools, instantly.
            Unregister(strategy, catalog, "conn-a1");
            var watch = Stopwatch.StartNew();
            var offlineList = await host.CallAsync(http, sessionA, "tools/list", id: 3);
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            offlineList.ShouldContain("engine_tool");
            offlineList.ShouldContain(ServerNativeToolsName);

            // Account B never sees them, and its tool call fails at once with the usual error.
            var tokenB = host.TokenForAccount(AccountB);
            var sessionB = await HandshakeAsync(host, http, tokenB);
            var listB = await host.CallAsync(http, sessionB, "tools/list", id: 4, bearerToken: tokenB);
            listB.ShouldContain(ServerNativeToolsName, customMessage: "control: account B got a real tool list");
            listB.ShouldNotContain("engine_tool");
            watch.Restart();
            var callB = NoneAuthMcpHost.Unwrap((await host.PostAsync(http, sessionB, ToolsCall(5), bearerToken: tokenB)).Body);
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
            callB.ShouldContain("\"isError\":true");

            // Account A's tool call waits for the plugin and is sent once, to the new connection.
            var callA = host.PostAsync(http, sessionA, ToolsCall(6));
            await Task.Delay(500);
            callA.IsCompleted.ShouldBeFalse("the call must wait for the reconnecting plugin");
            var newPlugin = Plugin("conn-a2", callAnswer: "from-new-connection");
            Register(strategy, catalog, "conn-a2");
            var bodyA = NoneAuthMcpHost.Unwrap((await callA.WaitAsync(TimeSpan.FromSeconds(15))).Body);

            bodyA.ShouldContain("from-new-connection");
            newPlugin.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once());
            _plugins["conn-a1"].Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task NoneAuth_NoPluginConnected_ToolsListAnswersAtOnce()
        {
            await using var host = await NoneAuthMcpHost.StartAsync();
            using var http = new HttpClient();
            var session = await host.HandshakeAsync(http);

            var watch = Stopwatch.StartNew();
            var list = await host.CallAsync(http, session, "tools/list");

            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3), "nothing is reconnecting, so the old ~10 s retry must not run");
            list.ShouldContain("\"tools\"");
        }

        /// <summary>
        /// Pins that a deploy does not keep plugins on the old instance: SignalR closes the hub
        /// connections when the host starts stopping (with the 2 s WebSocket close timeout set in
        /// <c>UseMcpPluginServer</c>), so the plugin reconnects to the new instance at once.
        /// </summary>
        [Fact]
        public async Task HostShutdown_ClosesPluginConnectionsPromptly()
        {
            var host = await NoneAuthMcpHost.StartAsync();
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var plugin = new HubConnectionBuilder()
                .WithUrl(host.BaseUrl + Common.Consts.Hub.RemoteApp)
                .Build();
            plugin.Closed += _ => { closed.TrySetResult(true); return Task.CompletedTask; };
            await plugin.StartAsync();
            plugin.State.ShouldBe(HubConnectionState.Connected);

            var watch = Stopwatch.StartNew();
            await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));

            (await closed.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "shutdown must not wait for open plugin connections");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        const string ServerNativeToolsName = "list_engine_instances";

        IHubContext<McpServerHub> FakeHubContext()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Client(It.IsAny<string>()))
                .Returns((string id) => _plugins.TryGetValue(id, out var plugin) ? plugin.Object : null!);
            var hub = new Mock<IHubContext<McpServerHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);
            return hub.Object;
        }

        Mock<ISingleClientProxy> Plugin(string connectionId, string callAnswer)
        {
            var plugin = new Mock<ISingleClientProxy>();
            plugin.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ResponseData<ResponseListTool[]>.Success("r").SetData(PluginCatalogCacheTests.Tools("engine_tool")));
            plugin.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ResponseCallTool.Success(callAnswer).Pack("r"));
            _plugins[connectionId] = plugin;
            return plugin;
        }

        static void Register(AccountMcpStrategy strategy, PluginCatalogCache catalog, string connectionId)
        {
            strategy.RegisterInstance(new ConnectionIdentity(AccountA, ConnectionIdentity.RolePlugin),
                new PluginInstanceMetadata("instance-a", "unity", "MyGame", HashA, "PC-1"), connectionId, NullLogger.Instance);
            catalog.OnPluginConnected(connectionId);
        }

        static void Unregister(AccountMcpStrategy strategy, PluginCatalogCache catalog, string connectionId)
        {
            catalog.OnPluginDisconnected(connectionId);
            strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, NullLogger.Instance);
        }

        static object ToolsCall(int id) => new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new { name = "engine_tool", arguments = new { } }
        };

        static async Task<string> HandshakeAsync(OAuthMcpHost host, HttpClient http, string token)
        {
            var (_, sessionId) = await host.PostAsync(http, null, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "reconnect-test-b", version = "1.0.0" }
                }
            }, bearerToken: token);
            sessionId.ShouldNotBeNullOrEmpty();
            await host.PostAsync(http, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" }, bearerToken: token);
            return sessionId!;
        }
    }
}
