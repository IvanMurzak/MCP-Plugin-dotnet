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
using System.Linq;
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
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The system-tools <c>ping</c> liveness probe answers 503 at once when no engine plugin is connected, instead of
    /// waiting through the ~10 s retry ladder (HTTP 499 in production). Every other request keeps waiting.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class SystemToolPingFailFastTests
    {
        const string Account = "acct-ping-fail-fast";
        const string PinA = "aabbccdd"; // prefix of PluginReconnect.HashA
        const string WrongPin = "deadbeef";

        // Well under the ~9 s retry ladder, with room for a cold host on a slow CI runner.
        static readonly TimeSpan Fast = TimeSpan.FromSeconds(3);

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
            ConnectProjectA(host);
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
            var strategy = PluginReconnect.StrategyResolving(null);
            using var runner = SystemRunner(strategy.Object);
            using var request = PingRequest();

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
            var plugin = _plugins.AddAnsweringCalls("c1");
            using var runner = SystemRunner(PluginReconnect.StrategyResolving("c1").Object);
            using var request = PingRequest();

            var result = await runner.RunSystemTool(request).WaitAsync(TimeSpan.FromSeconds(10));

            result.Status.ShouldBe(ResponseStatus.Success);
            FakePluginHub.VerifyCalls(plugin, Times.Once());
        }

        // ─────────────── usage webhooks ───────────────

        [Fact]
        public async Task PinnedPing_NoPlugin_IsNotReportedToTheToolWebhook()
        {
            var collector = new Mock<IWebhookEventCollector>();
            await using var host = await OAuthMcpHost.StartAsync(services => WithToolWebhook(services, collector), Account);
            using var http = new HttpClient();

            var (status, error) = await PingAsync(http, host, $"/p/{PinA}/api/system-tools/ping");

            status.ShouldBe(HttpStatusCode.ServiceUnavailable);
            error.ShouldStartWith("No game engine is connected for your account");
            collector.Verify(c => c.OnToolCall(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never());
        }

        [Fact]
        public async Task PinnedPing_PluginConnected_IsReportedToTheToolWebhookAsSuccess()
        {
            var collector = new Mock<IWebhookEventCollector>();
            await using var host = await OAuthMcpHost.StartAsync(services => WithToolWebhook(services, collector), Account);
            ConnectProjectA(host);
            using var http = new HttpClient();

            var (status, _) = await PingAsync(http, host, $"/p/{PinA}/api/system-tools/ping");

            status.ShouldBe(HttpStatusCode.OK);
            collector.Verify(c => c.OnToolCall(RemoteSystemToolRunner.LivenessProbeToolName, It.IsAny<long>(), It.IsAny<long>(),
                "success", It.IsAny<long>(), It.IsAny<string?>(), "http"), Times.Once());
        }

        [Theory]
        [InlineData("ping", ResponseErrorKind.Unavailable, 503, true)]   // no plugin connected (or one refused it unrun)
        [InlineData("ping", ResponseErrorKind.Unavailable, 502, false)]  // the connection failed after the send
        [InlineData("ping", ResponseErrorKind.Unavailable, null, false)] // the plugin's own Unavailable error
        [InlineData("ping", ResponseErrorKind.Timeout, null, false)]
        [InlineData("other_system_tool", ResponseErrorKind.Unavailable, 503, false)]
        public void IsProbeWithoutPlugin_MatchesOnlyTheProbesNotConnectedAnswer(string name, ResponseErrorKind kind, int? status, bool expected)
        {
            var response = ResponseCallTool.Error("x", kind, status).Pack("request-id");

            RemoteSystemToolRunner.IsProbeWithoutPlugin(name, response).ShouldBe(expected);
        }

        // ─────────────── everything else still waits ───────────────

        [Fact]
        public async Task ToolCall_PluginReconnecting_StillWaitsAndIsSentOnce_WhilePingAnswersAtOnce()
        {
            var strategy = new AccountMcpStrategy();
            var catalog = new PluginCatalogCache(strategy, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            var arguments = PluginReconnect.Arguments(toolTimeoutMs: 60_000);
            using var tools = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _plugins.Context, arguments, PluginReconnect.Tracking(), strategy, catalog);
            using var system = new RemoteSystemToolRunner(NullLogger<RemoteSystemToolRunner>.Instance, _plugins.Context, arguments, PluginReconnect.Tracking(), strategy);
            var oldPlugin = _plugins.AddAnsweringCalls("c1");
            PluginReconnect.Register(strategy, catalog, Account, "i1", "c1");
            catalog.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(strategy, catalog, "c1"); // the plugin drops (a recompile)

            using (PluginReconnect.Session(Account, PinA))
            {
                using var callRequest = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                var call = tools.RunCallTool(callRequest);

                using var pingRequest = PingRequest();
                var watch = Stopwatch.StartNew();
                var ping = await system.RunSystemTool(pingRequest).WaitAsync(TimeSpan.FromSeconds(30));
                watch.Elapsed.ShouldBeLessThan(Fast);
                ping.Value!.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);

                await Task.Delay(500);
                call.IsCompleted.ShouldBeFalse("a tool call must keep waiting for the reconnecting plugin");
                var newPlugin = _plugins.AddAnsweringCalls("c2");
                PluginReconnect.Register(strategy, catalog, Account, "i1", "c2");
                var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

                result.Status.ShouldBe(ResponseStatus.Success);
                FakePluginHub.VerifyCalls(newPlugin, Times.Once());
            }
            FakePluginHub.VerifyCalls(oldPlugin, Times.Never());
        }

        [Fact]
        public async Task OtherSystemTool_NoPlugin_StillWaitsThroughTheRetryLadder()
        {
            var strategy = PluginReconnect.StrategyResolving(null);
            using var runner = SystemRunner(strategy.Object);
            using var request = new RequestCallTool("other_system_tool", new Dictionary<string, JsonElement>());
            using var cts = new CancellationTokenSource();

            var call = runner.RunSystemTool(request, cts.Token);
            var retried = Stopwatch.StartNew();
            while (!Retried(strategy) && retried.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);

            Retried(strategy).ShouldBeTrue("the retry ladder runs for other system tools");
            call.IsCompleted.ShouldBeFalse("only the liveness probe skips the wait for a plugin");
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
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json", $"HTTP {(int)response.StatusCode}: {body}");
            using var json = JsonDocument.Parse(body);
            var error = json.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            return (response.StatusCode, error);
        }

        void WithToolWebhook(IServiceCollection services, Mock<IWebhookEventCollector> collector)
        {
            services.AddSingleton(_plugins.Context);
            services.AddSingleton(new WebhookOptions(toolWebhookUrl: "http://127.0.0.1:9/tools"));
            services.AddSingleton(collector.Object);
        }

        /// <summary>Connects an answering plugin for <see cref="PinA"/> to the host's account.</summary>
        void ConnectProjectA(OAuthMcpHost host)
        {
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();
            var catalog = host.Services.GetRequiredService<PluginCatalogCache>();
            _plugins.AddAnsweringCalls("conn-a");
            PluginReconnect.Register(strategy, catalog, Account, "instance-a", "conn-a");
        }

        static RequestCallTool PingRequest() => new RequestCallTool(RemoteSystemToolRunner.LivenessProbeToolName, new Dictionary<string, JsonElement>());

        static bool Retried(Mock<IMcpConnectionStrategy> strategy)
            => strategy.Invocations.Any(i => i.Method.Name == nameof(IMcpConnectionStrategy.ResolveConnectionId) && (int)i.Arguments[1] > 0);

        RemoteSystemToolRunner SystemRunner(IMcpConnectionStrategy strategy)
            => new RemoteSystemToolRunner(NullLogger<RemoteSystemToolRunner>.Instance, _plugins.Context,
                PluginReconnect.Arguments(toolTimeoutMs: 60_000), PluginReconnect.Tracking(), strategy);
    }
}
