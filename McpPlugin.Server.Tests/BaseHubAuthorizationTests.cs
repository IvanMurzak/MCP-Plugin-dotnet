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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Server;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests
{
    /// <summary>
    /// Tests that BaseHub.OnConnectedAsync properly integrates with
    /// the IAuthorizationWebhookService to allow or reject connections.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class BaseHubAuthorizationTests
    {
        static string UniqueId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// Concrete test hub that exposes BaseHub internals for testing.
        /// </summary>
        class TestHub : BaseHub<IClientDisconnectable>
        {
            public TestHub(ILogger logger, IMcpConnectionStrategy strategy, IAuthorizationWebhookService authorizationWebhookService)
                : base(logger, strategy, authorizationWebhookService)
            {
            }

            public bool ConnectionRejectedFlag => _connectionRejected;
        }

        static Mock<HubCallerContext> CreateMockHubCallerContext(string? bearerToken = null)
        {
            var connectionId = UniqueId();
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
            mockContext.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

            // Set up HttpContext accessible via GetHttpContext() extension
            var httpContext = new DefaultHttpContext();
            if (bearerToken != null)
                httpContext.Request.Headers["Authorization"] = $"Bearer {bearerToken}";

            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
            mockContext.Setup(c => c.Features).Returns(features);

            return mockContext;
        }

        class HttpContextFeature : IHttpContextFeature
        {
            public HttpContext? HttpContext { get; set; }
        }

        static TestHub CreateTestHub(
            Mock<HubCallerContext> mockContext,
            Mock<IAuthorizationWebhookService>? webhook = null,
            Mock<IMcpConnectionStrategy>? strategy = null)
        {
            var logger = new Mock<ILogger>().Object;
            strategy ??= new Mock<IMcpConnectionStrategy>();
            webhook ??= new Mock<IAuthorizationWebhookService>();

            var hub = new TestHub(logger, strategy.Object, webhook.Object);
            hub.Context = mockContext.Object;
            hub.Clients = new Mock<IHubCallerClients<IClientDisconnectable>>().Object;
            return hub;
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookAllows_DoesNotReject()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var mockContext = CreateMockHubCallerContext();
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            hub.ConnectionRejectedFlag.ShouldBeFalse();
            mockContext.Verify(c => c.Abort(), Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookDenies_SetsRejectedFlagAndAborts()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var mockContext = CreateMockHubCallerContext();
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            hub.ConnectionRejectedFlag.ShouldBeTrue();
            mockContext.Verify(c => c.Abort(), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookDenies_DoesNotCallStrategy()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var strategy = new Mock<IMcpConnectionStrategy>();
            var mockContext = CreateMockHubCallerContext();
            var hub = CreateTestHub(mockContext, webhook, strategy);

            await hub.OnConnectedAsync();

            strategy.Verify(
                s => s.OnPluginConnected(
                    It.IsAny<Type>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<ILogger>(),
                    It.IsAny<Action<string, string?>>()),
                Times.Never);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookAllows_CallsStrategy()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var strategy = new Mock<IMcpConnectionStrategy>();
            var mockContext = CreateMockHubCallerContext();
            var hub = CreateTestHub(mockContext, webhook, strategy);

            await hub.OnConnectedAsync();

            strategy.Verify(
                s => s.OnPluginConnected(
                    It.IsAny<Type>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<ILogger>(),
                    It.IsAny<Action<string, string?>>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_PassesBearerTokenToWebhook()
        {
            var token = UniqueId();
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var mockContext = CreateMockHubCallerContext(bearerToken: token);
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            webhook.Verify(w => w.AuthorizePluginAsync(
                It.IsAny<string>(),
                token,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_NoToken_PassesNullTokenToWebhook()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var mockContext = CreateMockHubCallerContext(bearerToken: null);
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            webhook.Verify(w => w.AuthorizePluginAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_NoOpWebhook_AllowsConnection()
        {
            var webhook = new NoOpAuthorizationWebhookService();

            var logger = new Mock<ILogger>().Object;
            var strategy = new Mock<IMcpConnectionStrategy>();
            var mockContext = CreateMockHubCallerContext();

            var hub = new TestHub(logger, strategy.Object, webhook);
            hub.Context = mockContext.Object;
            hub.Clients = new Mock<IHubCallerClients<IClientDisconnectable>>().Object;

            await hub.OnConnectedAsync();

            hub.ConnectionRejectedFlag.ShouldBeFalse();
            mockContext.Verify(c => c.Abort(), Times.Never);
        }
    }
}
