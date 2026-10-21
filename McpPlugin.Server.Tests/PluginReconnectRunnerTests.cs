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
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
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
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";

        readonly AccountMcpStrategy _strategy = new AccountMcpStrategy();
        readonly ConcurrentDictionary<string, Mock<ISingleClientProxy>> _plugins = new ConcurrentDictionary<string, Mock<ISingleClientProxy>>();
        readonly IHubContext<McpServerHub> _hub;

        public PluginReconnectRunnerTests()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Client(It.IsAny<string>()))
                .Returns((string id) => _plugins.TryGetValue(id, out var plugin) ? plugin.Object : null!);
            var hub = new Mock<IHubContext<McpServerHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);
            _hub = hub.Object;
        }

        [Fact]
        public async Task ListCalls_NoPluginAndNothingRemembered_AnswerAtOnce()
        {
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.Setup(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>())).Returns((string?)null);
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
                .ReturnsAsync(ResponseData<ResponseListTool[]>.Success("r").SetData(PluginCatalogCacheTests.Tools("engine_tool")));
            Register("i1", "c1", catalog);

            using (PluginCatalogCacheTests.Session(Account))
            {
                (await runner.RunListTool(new RequestListTool())).Value!.ShouldHaveSingleItem().Name.ShouldBe("engine_tool");

                Unregister("c1", catalog);
                var watch = Stopwatch.StartNew();
                var offline = await runner.RunListTool(new RequestListTool());

                watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
                offline.Status.ShouldBe(ResponseStatus.Success);
                offline.Value!.ShouldHaveSingleItem().Name.ShouldBe("engine_tool");
            }
            using (PluginCatalogCacheTests.Session("another-account"))
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
            using (PluginCatalogCacheTests.Session(Account))
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
            using (PluginCatalogCacheTests.Session(Account))
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

        [Fact]
        public async Task ToolCall_CallerCancelsWhileWaitingForTheReconnect_StopsTheWait()
        {
            var catalog = Catalog(TimeSpan.FromSeconds(30));
            using var runner = Runner(catalog);
            PluginAnsweringCalls("c1");
            RememberToolsOf("i1", "c1", catalog);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            using (PluginCatalogCacheTests.Session(Account))
            {
                using var request = new RequestCallTool("engine_tool", new Dictionary<string, JsonElement>());
                await Should.ThrowAsync<OperationCanceledException>(() => runner.RunCallTool(request, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            }
        }

        // ───────────────────────────── helpers ─────────────────────────────

        PluginCatalogCache Catalog(TimeSpan wait) => new PluginCatalogCache(_strategy, TimeSpan.FromMinutes(5), wait);

        RemoteToolRunner Runner(PluginCatalogCache catalog)
            => new RemoteToolRunner(NullLogger<RemoteToolRunner>.Instance, _hub, Arguments(), Tracking(), _strategy, catalog);

        static IDataArguments Arguments()
        {
            var arguments = new Mock<IDataArguments>();
            arguments.Setup(a => a.PluginTimeoutMs).Returns(2_000);
            arguments.As<IToolTimeoutArguments>().Setup(a => a.ToolTimeoutMs).Returns(10_000);
            return arguments.Object;
        }

        static RequestTrackingService Tracking() => new RequestTrackingService(NullLogger<RequestTrackingService>.Instance);

        Mock<ISingleClientProxy> Plugin(string connectionId)
        {
            var plugin = new Mock<ISingleClientProxy>();
            _plugins[connectionId] = plugin;
            return plugin;
        }

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
            catalog.Record(connectionId, PluginCatalogKind.Tools, PluginCatalogCacheTests.Tools("engine_tool"));
            Unregister(connectionId, catalog);
        }

        void Register(string instanceId, string connectionId, PluginCatalogCache catalog)
        {
            _strategy.RegisterInstance(new ConnectionIdentity(Account, ConnectionIdentity.RolePlugin),
                new PluginInstanceMetadata(instanceId, "unity", "MyGame", HashA, "PC-1"), connectionId, NullLogger.Instance);
            catalog.OnPluginConnected(connectionId);
        }

        void Unregister(string connectionId, PluginCatalogCache catalog)
        {
            catalog.OnPluginDisconnected(connectionId);
            _strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, NullLogger.Instance);
        }

        static void VerifyCalls(Mock<ISingleClientProxy> plugin, Times times)
            => plugin.Verify(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), times);
    }
}
