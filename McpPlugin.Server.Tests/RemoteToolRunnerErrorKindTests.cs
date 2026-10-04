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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The error kind a plugin reports must survive the server's remote runners: the direct REST
    /// endpoint picks its status from it, so a runner that rewrote a tool's own <c>BadRequest</c>
    /// to <c>Internal</c> would turn every declined call back into a 500. The endpoint tests stub
    /// <see cref="IClientToolHub"/> out entirely, so only this class drives the real runners.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class RemoteToolRunnerErrorKindTests
    {
        public static IEnumerable<object[]> PluginErrors() => new[]
        {
            // A tool's own refusal, built the way a plugin builds it: no explicit kind.
            new object[] { ResponseCallTool.Error("No Renderers found").Pack("request-1"), ResponseErrorKind.BadRequest },
            new object[] { ResponseCallTool.Error("missing", ResponseErrorKind.NotFound).Pack("request-1"), ResponseErrorKind.NotFound },
            new object[] { ResponseCallTool.Error(new InvalidOperationException("boom")).Pack("request-1"), ResponseErrorKind.Internal },
            // A plugin that predates error kinds sends none; the runner treats it as a server-side failure.
            new object[] { ResponseData<ResponseCallTool>.Error("request-1", "legacy failure"), ResponseErrorKind.Internal },
        };

        [Theory]
        [MemberData(nameof(PluginErrors))]
        public async Task RunCallTool_KeepsThePluginsErrorKind(ResponseData<ResponseCallTool> pluginResponse, ResponseErrorKind expectedKind)
        {
            using var runner = new RemoteToolRunner(
                NullLogger<RemoteToolRunner>.Instance,
                HubContextAnswering(pluginResponse),
                Mock.Of<IDataArguments>(d => d.PluginTimeoutMs == 30_000),
                new RequestTrackingService(NullLogger<RequestTrackingService>.Instance),
                StrategyResolvingTo(ConnectionId));
            using var request = new RequestCallTool("test", new Dictionary<string, JsonElement>());

            var response = await runner.RunCallTool(request);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(expectedKind);
        }

        [Theory]
        [MemberData(nameof(PluginErrors))]
        public async Task RunSystemTool_KeepsThePluginsErrorKind(ResponseData<ResponseCallTool> pluginResponse, ResponseErrorKind expectedKind)
        {
            using var runner = new RemoteSystemToolRunner(
                NullLogger<RemoteSystemToolRunner>.Instance,
                HubContextAnswering(pluginResponse),
                Mock.Of<IDataArguments>(d => d.PluginTimeoutMs == 30_000),
                new RequestTrackingService(NullLogger<RequestTrackingService>.Instance),
                StrategyResolvingTo(ConnectionId));
            using var request = new RequestCallTool("test", new Dictionary<string, JsonElement>());

            var response = await runner.RunSystemTool(request);

            response.Status.ShouldBe(ResponseStatus.Error);
            response.ErrorKind.ShouldBe(expectedKind);
        }

        // Unique per run: ClientUtils remembers the last successful connection in a static, and a
        // shared id could let this class's entry be read by another test's routing.
        static readonly string ConnectionId = "remote-runner-error-kind-" + Guid.NewGuid().ToString("N");

        static IHubContext<McpServerHub> HubContextAnswering(ResponseData<ResponseCallTool> pluginResponse)
        {
            var client = new Mock<ISingleClientProxy>();
            client
                .Setup(c => c.InvokeCoreAsync<ResponseData<ResponseCallTool>>(
                    It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(pluginResponse);

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Client(ConnectionId)).Returns(client.Object);

            var hubContext = new Mock<IHubContext<McpServerHub>>();
            hubContext.Setup(h => h.Clients).Returns(clients.Object);
            return hubContext.Object;
        }

        static IMcpConnectionStrategy StrategyResolvingTo(string connectionId)
        {
            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.Setup(s => s.ResolveConnectionId(It.IsAny<string?>(), It.IsAny<int>())).Returns(connectionId);
            return strategy.Object;
        }
    }
}
