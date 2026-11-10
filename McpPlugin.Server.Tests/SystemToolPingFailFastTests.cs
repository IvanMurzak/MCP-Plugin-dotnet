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
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The system-tools <c>ping</c> is a liveness probe (the App polls it every ~30 s with a 2 s budget). With no
    /// engine plugin connected it answers 503 at once, instead of waiting through the ~10 s retry ladder that
    /// outlived the probe (HTTP 499 in production). Every other request keeps waiting for a reconnecting plugin.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class SystemToolPingFailFastTests
    {
        const string Account = "acct-ping-fail-fast";
        const string PinA = "aabbccdd"; // prefix of PluginReconnect.HashA
        const string WrongPin = "deadbeef";

        static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(900); // under the ladder's first 1 s retry delay

        readonly FakePluginHub _plugins = new FakePluginHub();

        // ─────────────── the probe answers at once ───────────────

        [Fact]
        public async Task PinnedPing_AccountHasNoPlugin_Answers503AtOnce_WithTheAccountEmptyText()
        {
            await using var host = await OAuthMcpHost.StartAsync(services => services.AddSingleton(_plugins.Context), Account);
            using var http = new HttpClient();

            var watch = Stopwatch.StartNew();
            var (status, error) = await PingAsync(http, host, $"/p/{PinA}/api/system-tools/ping");

            watch.Elapsed.ShouldBeLessThan(Fast, "a liveness probe must not wait through the retry ladder");
            status.ShouldBe(HttpStatusCode.ServiceUnavailable);
            // The App's registration probe classifies this text as `account-empty` (not connected).
            error.ShouldStartWith("No game engine is connected for your account");
        }

        [Fact]
        public async Task PinnedPing_OtherProjectConnected_Answers503AtOnce_WithThePinnedNoMatchText()
        {
            await using var host = await OAuthMcpHost.StartAsync(services => services.AddSingleton(_plugins.Context), Account);
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            PluginAnswering("conn-a");
            PluginReconnect.Register(strategy, catalog, Account, "instance-a", "conn-a");
            using var http = new HttpClient();

            // Control: the pinned project's own plugin answers the probe.
            var (okStatus, _) = await PingAsync(http, host, $"/p/{PinA}/api/system-tools/ping");
            okStatus.ShouldBe(HttpStatusCode.OK);

            var watch = Stopwatch.StartNew();
            var (status, error) = await PingAsync(http, host, $"/p/{WrongPin}/api/system-tools/ping");

            watch.Elapsed.ShouldBeLessThan(Fast);
            status.ShouldBe(HttpStatusCode.ServiceUnavailable, "503, never 404: a 404 sends the App to its legacy-route fallback");
            // The App's registration probe classifies this text as `no-match-pinned` (not connected).
            error.ShouldStartWith("The engine for this project is not connected");
        }

        [Fact]
        public async Task Ping_NoPluginWithoutAccountRouting_AnswersAtOnce_WithoutRetrying()
        {
            var strategy = StrategyResolving(null);
            using var runner = SystemRunner(strategy.Object);
            using var request = new RequestCallTool(RemoteSystemToolRunner.LivenessProbeToolName, new Dictionary<string, JsonElement>());

            var watch = Stopwatch.StartNew();
            var result = await runner.RunSystemTool(request).WaitAsync(TimeSpan.FromSeconds(30));

            watch.Elapsed.ShouldBeLessThan(Fast);
            result.Status.ShouldBe(ResponseStatus.Error);
            result.Value!.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            result.Message.ShouldNotBeNull().ShouldContain("No engine plugin is connected");
            strategy.Verify(s => s.ResolveConnectionId(It.IsAny<string?>(), It.Is<int>(offset => offset > 0)), Times.Never());
        }

        [Fact]
        public async Task Ping_PluginConnected_IsSentToThePluginOnce()
        {
            var plugin = PluginAnswering("c1");
            using var runner = SystemRunner(StrategyResolving("c1").Object);
            using var request = new RequestCallTool(RemoteSystemToolRunner.LivenessProbeToolName, new Dictionary<string, JsonElement>());

            var result = await runner.RunSystemTool(request).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Success);
            VerifyCalls(plugin, Times.Once());
        }

        // ─────────────── everything else still waits ───────────────

        [Fact]
        public async Task ToolCall_PluginReconnecting_StillWaitsAndIsSentOnce_WhilePingAnswersAtOnce()
        {
            var strategy = new AccountMcpStrategy();
            var catalog = new PluginCatalogCache(strategy, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            var arguments = Arguments();
            using var tools = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _plugins.Context, arguments, Tracking(), strategy, catalog);
            using var system = new RemoteSystemToolRunner(NullLogger<RemoteSystemToolRunner>.Instance, _plugins.Context, arguments, Tracking(), strategy);
            var oldPlugin = PluginAnswering("c1");
            PluginReconnect.Register(strategy, catalog, Account, "i1", "c1");
            catalog.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(strategy, catalog, "c1"); // the plugin drops (a recompile)

            using (PluginReconnect.Session(Account, PinA))
            {
                using var callRequest = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                var call = tools.RunCallTool(callRequest);

                using var pingRequest = new RequestCallTool(RemoteSystemToolRunner.LivenessProbeToolName, new Dictionary<string, JsonElement>());
                var watch = Stopwatch.StartNew();
                var ping = await system.RunSystemTool(pingRequest).WaitAsync(TimeSpan.FromSeconds(30));
                watch.Elapsed.ShouldBeLessThan(Fast);
                ping.Value!.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);

                await Task.Delay(500);
                call.IsCompleted.ShouldBeFalse("a tool call must keep waiting for the reconnecting plugin");
                var newPlugin = PluginAnswering("c2");
                PluginReconnect.Register(strategy, catalog, Account, "i1", "c2");
                var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

                result.Status.ShouldBe(ResponseStatus.Success);
                VerifyCalls(newPlugin, Times.Once());
            }
            VerifyCalls(oldPlugin, Times.Never());
        }

        [Fact]
        public async Task OtherSystemTool_NoPlugin_StillWaitsThroughTheRetryLadder()
        {
            var strategy = StrategyResolving(null);
            using var runner = SystemRunner(strategy.Object);
            using var request = new RequestCallTool("other_system_tool", new Dictionary<string, JsonElement>());
            using var cts = new CancellationTokenSource();

            var call = runner.RunSystemTool(request, cts.Token);
            await Task.Delay(1500);

            call.IsCompleted.ShouldBeFalse("only the liveness probe skips the wait for a plugin");
            strategy.Verify(s => s.ResolveConnectionId(It.IsAny<string?>(), 1), Times.Once(), "the ladder retried");
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static async Task<(HttpStatusCode Status, string Error)> PingAsync(HttpClient http, OAuthMcpHost host, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, host.BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.TokenForAccount(host.AccountId));
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            return (response.StatusCode, error);
        }

        RemoteSystemToolRunner SystemRunner(IMcpConnectionStrategy strategy)
            => new RemoteSystemToolRunner(NullLogger<RemoteSystemToolRunner>.Instance, _plugins.Context, Arguments(), Tracking(), strategy);

        static IDataArguments Arguments()
        {
            var arguments = new Mock<IDataArguments>();
            arguments.Setup(a => a.PluginTimeoutMs).Returns(2_000);
            arguments.As<IToolTimeoutArguments>().Setup(a => a.ToolTimeoutMs).Returns(60_000);
            return arguments.Object;
        }

        static Mock<IMcpConnectionStrategy> StrategyResolving(string? connectionId)
        {
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.Setup(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>())).Returns(connectionId);
            return strategy;
        }

        static RequestTrackingService Tracking() => new RequestTrackingService(NullLogger<RequestTrackingService>.Instance);

        Mock<ISingleClientProxy> PluginAnswering(string connectionId)
        {
            var plugin = _plugins.Add(connectionId);
            plugin.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ResponseCallTool.Success("pong").Pack("r"));
            return plugin;
        }

        static void VerifyCalls(Mock<ISingleClientProxy> plugin, Times times)
            => plugin.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);
    }
}
