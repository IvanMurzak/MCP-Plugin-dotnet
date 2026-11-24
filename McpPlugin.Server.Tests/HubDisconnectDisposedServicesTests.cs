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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// GlitchTip 1578: SignalR can run <see cref="McpServerHub.OnDisconnectedAsync"/> after the request
    /// that carried the connection ended and its services were disposed. Reading the catalog from those
    /// services threw <see cref="ObjectDisposedException"/>, skipped the strategy cleanup (the plugin
    /// stayed registered) and surfaced as an unobserved task exception.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class HubDisconnectDisposedServicesTests
    {
        sealed class HttpContextFeature : IHttpContextFeature
        {
            public HttpContext? HttpContext { get; set; }
        }

        [Fact]
        public async Task OnDisconnectedAsync_RequestServicesDisposed_StillCleansUpStrategyAndCatalog()
        {
            var connectionId = "disposed-sp-" + Guid.NewGuid().ToString("N");
            var strategy = new NoAuthMcpStrategy();
            var catalog = new PluginCatalogCache(strategy, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));

            var requestServices = new ServiceCollection().AddSingleton(catalog).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = requestServices };
            http.Request.QueryString = new QueryString("?" + Consts.MCP.Server.HubQuery.ClientRole + "=editor");
            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });
            var context = new Mock<HubCallerContext>();
            context.SetupGet(x => x.Features).Returns(features);
            context.SetupGet(x => x.ConnectionId).Returns(connectionId);
            context.SetupGet(x => x.ConnectionAborted).Returns(CancellationToken.None);
            context.SetupGet(x => x.Items).Returns(new Dictionary<object, object?>());

            var caller = new Mock<IClientMcpRpc>();
            caller.Setup(x => x.OnInitialClientData(It.IsAny<McpClientData[]>())).Returns(Task.CompletedTask);
            var clients = new Mock<IHubCallerClients<IClientMcpRpc>>();
            clients.SetupGet(x => x.Caller).Returns(caller.Object);

            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(true);
            var dataArguments = new Mock<IDataArguments>();
            dataArguments.SetupGet(x => x.ClientTransport).Returns(Consts.MCP.Server.TransportMethod.stdio);
            var version = new Common.Version { Api = "1.0.0", Plugin = "1.0.0" };
            var tracker = new McpSessionTracker(NullLogger<McpSessionTracker>.Instance, dataArguments.Object, version);

            // SignalR creates a hub instance per invocation; the connection's Items outlive them.
            McpServerHub NewHub() => new McpServerHub(
                NullLogger<McpServerHub>.Instance, version, dataArguments.Object,
                new HubEventToolsChange(), new HubEventPromptsChange(), new HubEventResourcesChange(),
                new Mock<IRequestTrackingService>().Object, tracker, strategy,
                new Mock<IWebhookEventCollector>().Object, webhook.Object)
            {
                Context = context.Object,
                Clients = clients.Object
            };

            try
            {
                using (var connectHub = NewHub())
                    await connectHub.OnConnectedAsync();
                ClientUtils.IsConnected(typeof(McpServerHub), connectionId).ShouldBeTrue();
                catalog.Record(connectionId, PluginCatalogKind.Tools, Array.Empty<ResponseListTool>());

                // The transport request ended: its services are gone before the disconnect runs.
                await requestServices.DisposeAsync();
                Should.Throw<ObjectDisposedException>(() => requestServices.GetService(typeof(PluginCatalogCache)));

                using (var disconnectHub = NewHub())
                    await Should.NotThrowAsync(() => disconnectHub.OnDisconnectedAsync(null));

                ClientUtils.IsConnected(typeof(McpServerHub), connectionId).ShouldBeFalse("the strategy cleanup must run");
                catalog.OfflineCount.ShouldBe(1, "the catalog keeps the plugin's lists for its reconnect grace");
            }
            finally
            {
                strategy.OnPluginDisconnected(typeof(McpServerHub), connectionId, NullLogger.Instance);
            }
        }
    }
}
