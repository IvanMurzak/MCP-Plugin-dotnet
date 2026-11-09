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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The remote runners with a <see cref="PluginCatalogCache"/>: a list never waits for a plugin that
    /// is not there, a reconnecting plugin's tools are served from memory, and a tool call waits for
    /// the plugin and is sent once — or fails with a clear "reconnecting" error.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginReconnectRunnerTests
    {
        const string Account = "acc-runner";

        readonly AccountMcpStrategy _strategy = new AccountMcpStrategy();
        readonly FakePluginHub _plugins = new FakePluginHub();
        IHubContext<McpServerHub> _hub => _plugins.Context;

        [Fact]
        public async Task ListCalls_NoPluginAndNothingRemembered_AnswerAtOnce()
        {
            var strategy = StrategyResolving(null);
            var catalog = new PluginCatalogCache(strategy.Object, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            var arguments = Arguments();
            using var tools = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            using var resources = new RemoteResourceRunner(NullLogger<RemoteResourceRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            var watch = Stopwatch.StartNew();

            var toolList = await tools.RunListTool(new RequestListTool());
            var promptList = await prompts.RunListPrompts(new RequestListPrompts());
            var resourceList = await resources.RunListResources(new RequestListResources());
            var templateList = await resources.RunResourceTemplates(new RequestListResourceTemplates());

            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1), "nothing is reconnecting, so there is nothing to wait for");
            toolList.Status.ShouldBe(ResponseStatus.Error);
            toolList.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            promptList.Status.ShouldBe(ResponseStatus.Error);
            resourceList.Status.ShouldBe(ResponseStatus.Error);
            templateList.Status.ShouldBe(ResponseStatus.Error);
        }

        [Fact]
        public async Task ListTools_PluginReconnecting_IsAnsweredFromMemory_WithoutAskingThePlugin()
        {
            var catalog = Catalog(TimeSpan.FromSeconds(30));
            using var runner = Runner(catalog);
            var plugin = Plugin("c1");
            plugin.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ResponseData<ResponseListTool[]>.Success("r").SetData(PluginReconnect.Tools("engine_tool")));
            Register("i1", "c1", catalog);

            using (PluginReconnect.Session(Account))
            {
                (await runner.RunListTool(new RequestListTool())).Value!.ShouldHaveSingleItem().Name.ShouldBe("engine_tool");

                Unregister("c1", catalog);
                var watch = Stopwatch.StartNew();
                var offline = await runner.RunListTool(new RequestListTool());

                watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
                offline.Status.ShouldBe(ResponseStatus.Success);
                offline.Value!.ShouldHaveSingleItem().Name.ShouldBe("engine_tool");
            }
            using (PluginReconnect.Session("another-account"))
                (await runner.RunListTool(new RequestListTool())).Status.ShouldBe(ResponseStatus.Error, "never another tenant's tools");

            plugin.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseListTool[]>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        [Fact]
        public async Task ToolCall_PluginReconnecting_WaitsAndIsSentOnceToTheNewConnection()
        {
            var catalog = Catalog(TimeSpan.FromSeconds(30));
            using var runner = Runner(catalog);
            var oldPlugin = PluginAnsweringCalls("c1");
            RememberToolsOf("i1", "c1", catalog);
            var newPlugin = PluginAnsweringCalls("c2");

            ResponseData<ResponseCallTool> result;
            using (PluginReconnect.Session(Account))
            {
                using var request = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                var call = runner.RunCallTool(request);
                await Task.Delay(400);
                call.IsCompleted.ShouldBeFalse("the call must wait for the reconnecting plugin");
                Register("i1", "c2", catalog);
                result = await call.WaitAsync(TimeSpan.FromSeconds(10));
            }

            result.Status.ShouldBe(ResponseStatus.Success);
            VerifyCalls(newPlugin, Times.Once());
            VerifyCalls(oldPlugin, Times.Never());
        }

        [Fact]
        public async Task ToolCall_PluginDoesNotReturnInTime_FailsWithAReconnectingError_AndIsNotSent()
        {
            var catalog = Catalog(TimeSpan.FromMilliseconds(500));
            using var runner = Runner(catalog);
            var oldPlugin = PluginAnsweringCalls("c1");
            RememberToolsOf("i1", "c1", catalog);

            ResponseData<ResponseCallTool> result;
            var watch = Stopwatch.StartNew();
            using (PluginReconnect.Session(Account))
            {
                using var request = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                result = await runner.RunCallTool(request).WaitAsync(TimeSpan.FromSeconds(10));
            }

            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            result.Status.ShouldBe(ResponseStatus.Error);
            result.Value!.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            result.Message.ShouldNotBeNull().ShouldContain("reconnecting");
            VerifyCalls(oldPlugin, Times.Never());
        }

        /// <summary>
        /// Past the grace nothing is remembered, so the fixed retry ladder runs. When it ends with no
        /// plugin on any attempt, every runner reports that with a text the agent can act on, under the
        /// same error kind.
        /// </summary>
        [Fact]
        public async Task PastGrace_NoPluginOnAnyAttempt_EveryRunnerSaysSo_AfterTheSameLadder()
        {
            var strategy = StrategyResolving(null);
            var catalog = new PluginCatalogCache(strategy.Object, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            var arguments = Arguments(toolTimeoutMs: 60_000); // longer than the ladder
            using var tools = new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            using var resources = new RemoteResourceRunner(NullLogger<RemoteResourceRunner>.Instance, _hub, arguments, Tracking(), strategy.Object, catalog);
            using var request = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
            var watch = Stopwatch.StartNew();

            var call = tools.RunCallTool(request);
            var prompt = prompts.RunGetPrompt(new RequestGetPrompt("engine_prompt", new Dictionary<string, JsonElement>()));
            var resource = resources.RunResourceContent(new RequestResourceContent("engine://resource"));
            var callResult = await call.WaitAsync(TimeSpan.FromSeconds(30));
            var promptResult = await prompt.WaitAsync(TimeSpan.FromSeconds(30));
            var resourceResult = await resource.WaitAsync(TimeSpan.FromSeconds(30));

            watch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(8), "the retry ladder itself must not change");
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(18), "the retry ladder itself must not change");
            strategy.Verify(s => s.ResolveConnectionId(It.IsAny<string?>(), 9), Times.Exactly(3));
            strategy.Verify(s => s.ResolveConnectionId(It.IsAny<string?>(), 10), Times.Never());
            foreach (var (status, kind, message) in new[]
            {
                (callResult.Status, callResult.Value!.ErrorKind, callResult.Message),
                (promptResult.Status, promptResult.ErrorKind, promptResult.Message),
                (resourceResult.Status, resourceResult.ErrorKind, resourceResult.Message),
            })
            {
                status.ShouldBe(ResponseStatus.Error);
                kind.ShouldBe(ResponseErrorKind.Unavailable);
                message.ShouldNotBeNull().ShouldContain("No engine plugin is connected");
                message.ShouldContain("engine editor");
            }
        }

        /// <summary>A plugin that was reached but kept failing is not reported as "not connected".</summary>
        [Fact]
        public async Task LadderEndsAfterReachingAPlugin_KeepsTheFailureText_NotTheNoPluginOne()
        {
            var strategy = StrategyResolving("c1");
            Plugin("c1").Setup(c => c.InvokeCoreAsync<ResponseData<ResponseGetPrompt>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broken"));
            var catalog = new PluginCatalogCache(strategy.Object, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, Arguments(), Tracking(), strategy.Object, catalog);

            var result = await prompts.RunGetPrompt(new RequestGetPrompt("engine_prompt", new Dictionary<string, JsonElement>()))
                .WaitAsync(TimeSpan.FromSeconds(30));

            result.Status.ShouldBe(ResponseStatus.Error);
            result.ErrorKind.ShouldBe(ResponseErrorKind.Internal);
            result.Message.ShouldNotBeNull().ShouldContain("Failed to invoke");
        }

        /// <summary>A connection that was already gone on every attempt never reached a plugin.</summary>
        [Fact]
        public async Task LadderEndsWithOnlyGoneConnections_SaysNoPluginIsConnected()
        {
            var strategy = StrategyResolving("c1");
            Plugin("c1").Setup(c => c.InvokeCoreAsync<ResponseData<ResponseGetPrompt>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Connection 'c1' does not exist."));
            var catalog = new PluginCatalogCache(strategy.Object, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, Arguments(), Tracking(), strategy.Object, catalog);

            var result = await prompts.RunGetPrompt(new RequestGetPrompt("engine_prompt", new Dictionary<string, JsonElement>()))
                .WaitAsync(TimeSpan.FromSeconds(30));

            result.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            result.Message.ShouldNotBeNull().ShouldContain("No engine plugin is connected");
        }

        [Fact]
        public async Task LadderEndsOnPluginTimeouts_KeepsTheTimeoutKindAndText()
        {
            var strategy = StrategyResolving("c1");
            Plugin("c1").Setup(c => c.InvokeCoreAsync<ResponseData<ResponseGetPrompt>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(new TaskCompletionSource<ResponseData<ResponseGetPrompt>>().Task); // never answers
            var catalog = new PluginCatalogCache(strategy.Object, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, Arguments(pluginTimeoutMs: 20), Tracking(), strategy.Object, catalog);

            var result = await prompts.RunGetPrompt(new RequestGetPrompt("engine_prompt", new Dictionary<string, JsonElement>()))
                .WaitAsync(TimeSpan.FromSeconds(30));

            result.ErrorKind.ShouldBe(ResponseErrorKind.Timeout);
            result.Message.ShouldNotBeNull().ShouldContain("Failed to invoke");
        }

        [Fact]
        public async Task PromptGet_PluginDoesNotReturnInTime_KeepsTheReconnectingError()
        {
            var catalog = Catalog(TimeSpan.FromMilliseconds(200));
            RememberToolsOf("i1", "c1", catalog);
            using var prompts = new RemotePromptRunner(NullLogger<RemotePromptRunner>.Instance, _hub, Arguments(), Tracking(), _strategy, catalog);

            ResponseData<ResponseGetPrompt> result;
            using (PluginReconnect.Session(Account))
                result = await prompts.RunGetPrompt(new RequestGetPrompt("engine_prompt", new Dictionary<string, JsonElement>()))
                    .WaitAsync(TimeSpan.FromSeconds(10));

            result.ErrorKind.ShouldBe(ResponseErrorKind.Unavailable);
            result.Message.ShouldNotBeNull().ShouldContain("reconnecting");
        }

        [Fact]
        public async Task ToolCall_CallerCancelsWhileWaitingForTheReconnect_StopsTheWait()
        {
            var catalog = Catalog(TimeSpan.FromSeconds(30));
            using var runner = Runner(catalog);
            PluginAnsweringCalls("c1");
            RememberToolsOf("i1", "c1", catalog);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            using (PluginReconnect.Session(Account))
            {
                using var request = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                await Should.ThrowAsync<OperationCanceledException>(() => runner.RunCallTool(request, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        PluginCatalogCache Catalog(TimeSpan wait) => new PluginCatalogCache(_strategy, TimeSpan.FromMinutes(5), wait);

        RemoteToolRunner Runner(PluginCatalogCache catalog)
            => new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _hub, Arguments(), Tracking(), _strategy, catalog);

        static IDataArguments Arguments(int toolTimeoutMs = 10_000, int pluginTimeoutMs = 2_000)
        {
            var arguments = new Mock<IDataArguments>();
            arguments.Setup(a => a.PluginTimeoutMs).Returns(pluginTimeoutMs);
            arguments.As<IToolTimeoutArguments>().Setup(a => a.ToolTimeoutMs).Returns(toolTimeoutMs);
            return arguments.Object;
        }

        static Mock<IMcpConnectionStrategy> StrategyResolving(string? connectionId)
        {
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.Setup(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>())).Returns(connectionId);
            return strategy;
        }

        static RequestTrackingService Tracking() => new RequestTrackingService(NullLogger<RequestTrackingService>.Instance);

        Mock<ISingleClientProxy> Plugin(string connectionId) => _plugins.Add(connectionId);

        Mock<ISingleClientProxy> PluginAnsweringCalls(string connectionId)
        {
            var plugin = Plugin(connectionId);
            plugin.Setup(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ResponseCallTool.Success("done").Pack("r"));
            return plugin;
        }

        /// <summary>Connects an instance, remembers its tools, then drops its connection.</summary>
        void RememberToolsOf(string instanceId, string connectionId, PluginCatalogCache catalog)
        {
            Register(instanceId, connectionId, catalog);
            catalog.Record(connectionId, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            Unregister(connectionId, catalog);
        }

        void Register(string instanceId, string connectionId, PluginCatalogCache catalog)
            => PluginReconnect.Register(_strategy, catalog, Account, instanceId, connectionId);

        void Unregister(string connectionId, PluginCatalogCache catalog)
            => PluginReconnect.Unregister(_strategy, catalog, connectionId);

        static void VerifyCalls(Mock<ISingleClientProxy> plugin, Times times)
            => plugin.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);
    }
}
