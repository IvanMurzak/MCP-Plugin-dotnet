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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class RequestActivityTests
    {
        static McpManager CreateManager(
            IToolManager? tools = null,
            IPromptManager? prompts = null,
            IResourceManager? resources = null,
            ISystemToolManager? systemTools = null)
            => new McpManager(NullLogger<McpManager>.Instance, new Reflector(), tools, prompts, resources, systemTools);

        [Fact]
        public async Task ToolCall_IsInFlight_UntilItsResponseIsReady()
        {
            var gate = new TaskCompletionSource<ResponseData<ResponseCallTool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new Mock<IToolManager>();
            tools.Setup(t => t.RunCallTool(It.IsAny<RequestCallTool>())).Returns(gate.Task);
            var manager = CreateManager(tools: tools.Object);
            var activity = manager.RequestActivity;

            activity.InFlightCount.ShouldBe(0);

            var call = manager.ToolHub!.RunCallTool(new RequestCallTool("slow", new Dictionary<string, JsonElement>()));

            activity.InFlightCount.ShouldBe(1);
            activity.IdleDuration.ShouldBe(TimeSpan.Zero);

            gate.SetResult(ResponseData<ResponseCallTool>.Success("id"));
            await call;

            activity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public async Task IdleDuration_IsMeasuredFromTheLastFinish()
        {
            var gate = new TaskCompletionSource<ResponseData<ResponseCallTool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new Mock<IToolManager>();
            tools.Setup(t => t.RunCallTool(It.IsAny<RequestCallTool>())).Returns(gate.Task);
            var manager = CreateManager(tools: tools.Object);
            var activity = manager.RequestActivity;

            var call = manager.ToolHub!.RunCallTool(new RequestCallTool("slow", new Dictionary<string, JsonElement>()));
            // The call runs longer than the gap below; a timer measuring from the start would already see it idle.
            await Task.Delay(500);
            activity.IdleDuration.ShouldBe(TimeSpan.Zero);

            gate.SetResult(ResponseData<ResponseCallTool>.Success("id"));
            await call;

            activity.IdleDuration.ShouldBeLessThan(TimeSpan.FromMilliseconds(400));
            await Task.Delay(100);
            activity.IdleDuration.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(80));
        }

        [Fact]
        public async Task FailedCalls_StillEndTheRequest()
        {
            var tools = new Mock<IToolManager>();
            tools.Setup(t => t.RunCallTool(It.IsAny<RequestCallTool>())).ThrowsAsync(new InvalidOperationException("async failure"));
            var systemTools = new Mock<ISystemToolManager>();
            systemTools.Setup(t => t.RunSystemTool(It.IsAny<RequestCallTool>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("sync failure"));
            var manager = CreateManager(tools: tools.Object, systemTools: systemTools.Object);
            var request = new RequestCallTool("broken", new Dictionary<string, JsonElement>());

            await Should.ThrowAsync<InvalidOperationException>(() => manager.ToolHub!.RunCallTool(request));
            await Should.ThrowAsync<InvalidOperationException>(() => manager.SystemToolHub!.RunSystemTool(request));

            manager.RequestActivity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public async Task PromptResourceAndSystemToolCalls_AreCounted_ListRequestsAreNot()
        {
            var prompts = new Mock<IPromptManager>();
            var resources = new Mock<IResourceManager>();
            var systemTools = new Mock<ISystemToolManager>();
            var tools = new Mock<IToolManager>();
            var manager = CreateManager(tools.Object, prompts.Object, resources.Object, systemTools.Object);
            var activity = manager.RequestActivity;
            var seen = new List<int>();

            prompts.Setup(p => p.RunGetPrompt(It.IsAny<RequestGetPrompt>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseGetPrompt>()); });
            resources.Setup(r => r.RunResourceContent(It.IsAny<RequestResourceContent>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseResourceContent[]>()); });
            systemTools.Setup(s => s.RunSystemTool(It.IsAny<RequestCallTool>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseCallTool>()); });

            tools.Setup(t => t.RunListTool(It.IsAny<RequestListTool>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseListTool[]>()); });
            prompts.Setup(p => p.RunListPrompts(It.IsAny<RequestListPrompts>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseListPrompts>()); });
            resources.Setup(r => r.RunListResources(It.IsAny<RequestListResources>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseListResource[]>()); });
            resources.Setup(r => r.RunResourceTemplates(It.IsAny<RequestListResourceTemplates>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseResourceTemplate[]>()); });
            systemTools.Setup(s => s.RunListSystemTool(It.IsAny<RequestListTool>(), It.IsAny<CancellationToken>()))
                .Returns(() => { seen.Add(activity.InFlightCount); return Task.FromResult(new ResponseData<ResponseListTool[]>()); });

            await manager.PromptHub!.RunGetPrompt(new RequestGetPrompt("p", new Dictionary<string, JsonElement>()));
            await manager.ResourceHub!.RunResourceContent(new RequestResourceContent("res://x"));
            await manager.SystemToolHub!.RunSystemTool(new RequestCallTool("s", new Dictionary<string, JsonElement>()));
            seen.ShouldBe(new[] { 1, 1, 1 });

            seen.Clear();
            await manager.ToolHub!.RunListTool(new RequestListTool());
            await manager.PromptHub.RunListPrompts(new RequestListPrompts());
            await manager.ResourceHub.RunListResources(new RequestListResources());
            await manager.ResourceHub.RunResourceTemplates(new RequestListResourceTemplates());
            await manager.SystemToolHub.RunListSystemTool(new RequestListTool());
            seen.ShouldBe(new[] { 0, 0, 0, 0, 0 });

            activity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public void BeginRequest_DisposingTwice_EndsTheRequestOnce()
        {
            var activity = new McpRequestActivity();
            var first = activity.BeginRequest();
            var second = activity.BeginRequest();

            first.Dispose();
            first.Dispose();
            activity.InFlightCount.ShouldBe(1);

            second.Dispose();
            activity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public async Task BeginRequest_IsThreadSafe()
        {
            var activity = new McpRequestActivity();

            await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++)
                    activity.BeginRequest().Dispose();
            })));

            activity.InFlightCount.ShouldBe(0);
        }

        [Fact]
        public async Task BuiltPlugin_ExposesTheManagersActivity_AndCountsServerToolCalls()
        {
            var builder = new McpPluginBuilder(new Version());
            builder.WithTool("testTool", "Test Tool", typeof(TestToolClass), typeof(TestToolClass).GetMethod(nameof(TestToolClass.TestMethod))!);
            var plugin = builder.Build(new Reflector());

            plugin.RequestActivity.ShouldNotBeNull();
            plugin.RequestActivity.ShouldBeSameAs(plugin.McpManager.RequestActivity);

            var hub = ((IClientMcpManager)plugin.McpManager).ToolHub!;
            var response = await hub.RunCallTool(new RequestCallTool("testTool", new Dictionary<string, JsonElement>()));

            response.Status.ShouldBe(ResponseStatus.Success);
            plugin.ToolCallsCount.ShouldBe(1UL);
            plugin.RequestActivity!.InFlightCount.ShouldBe(0);
        }

        private class TestToolClass
        {
            public string TestMethod() => "test result";
        }
    }
}
