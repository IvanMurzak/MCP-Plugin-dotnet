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
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Server;
using com.IvanMurzak.McpPlugin.Server.Auth;
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
        public async Task OnConnectedAsync_WebhookUnavailable_AbortsWithoutPermanentRejection_ThenReconnects()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.SetupSequence(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ThrowsAsync(new AuthorizationUnavailableException("Backend unavailable"))
                .ReturnsAsync(true);
            var strategy = new Mock<IMcpConnectionStrategy>();
            var caller = new Mock<IClientDisconnectable>();
            var clients = new Mock<IHubCallerClients<IClientDisconnectable>>();
            clients.Setup(x => x.Caller).Returns(caller.Object);
            var context = CreateMockHubCallerContext("same-token");
            var hub = CreateTestHub(context, webhook, strategy);
            hub.Clients = clients.Object;

            await hub.OnConnectedAsync();
            hub.ConnectionRejectedFlag.ShouldBeTrue();
            context.Verify(x => x.Abort(), Times.Once);
            caller.Verify(x => x.ForceDisconnect(It.IsAny<string?>()), Times.Never);
            strategy.Verify(x => x.OnPluginConnected(It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<ILogger>(), It.IsAny<Action<string, string?>>()), Times.Never);

            var nextContext = CreateMockHubCallerContext("same-token");
            var reconnected = CreateTestHub(nextContext, webhook, strategy);
            await reconnected.OnConnectedAsync();
            reconnected.ConnectionRejectedFlag.ShouldBeFalse();
            nextContext.Verify(x => x.Abort(), Times.Never);
            strategy.Verify(x => x.OnPluginConnected(It.IsAny<Type>(), nextContext.Object.ConnectionId, "same-token",
                It.IsAny<ILogger>(), It.IsAny<Action<string, string?>>()), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookAllows_DoesNotReject()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
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
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(false);

            var mockContext = CreateMockHubCallerContext();
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            hub.ConnectionRejectedFlag.ShouldBeTrue();
            mockContext.Verify(c => c.Abort(), Times.Once);
        }

        /// <summary>
        /// A denied plugin must actually RECEIVE ForceDisconnect: the send only queues the frame and an
        /// immediate Abort drops it, leaving the client a bare close that it treats as transient.
        /// </summary>
        [Fact]
        public async Task OnConnectedAsync_WebhookDenies_SendsForceDisconnect_ThenGivesItTimeToFlushBeforeAborting()
        {
            var (hub, context, caller) = DeniedHub(CancellationToken.None);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            TimeSpan? sentAt = null, abortedAt = null;
            caller.Setup(x => x.ForceDisconnect(It.IsAny<string?>()))
                .Callback(() => sentAt = clock.Elapsed)
                .Returns(Task.CompletedTask);
            context.Setup(c => c.Abort()).Callback(() => abortedAt = clock.Elapsed);

            await hub.OnConnectedAsync();

            sentAt.ShouldNotBeNull();
            abortedAt.ShouldNotBeNull();
            (abortedAt!.Value - sentAt!.Value).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(200),
                "Abort must not run right after the ForceDisconnect send");
            context.Verify(c => c.Abort(), Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookDenies_ClientAlreadyClosed_AbortsWithoutWaiting()
        {
            using var closed = new CancellationTokenSource();
            closed.Cancel();
            var (hub, context, caller) = DeniedHub(closed.Token);
            caller.Setup(x => x.ForceDisconnect(It.IsAny<string?>())).Returns(Task.CompletedTask);
            var clock = System.Diagnostics.Stopwatch.StartNew();

            await hub.OnConnectedAsync();

            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(200));
            caller.Verify(x => x.ForceDisconnect(It.IsAny<string?>()), Times.Once);
            context.Verify(c => c.Abort(), Times.Once);
            hub.ConnectionRejectedFlag.ShouldBeTrue();
        }

        static (TestHub Hub, Mock<HubCallerContext> Context, Mock<IClientDisconnectable> Caller) DeniedHub(CancellationToken connectionAborted)
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(false);
            var caller = new Mock<IClientDisconnectable>();
            var clients = new Mock<IHubCallerClients<IClientDisconnectable>>();
            clients.Setup(x => x.Caller).Returns(caller.Object);
            var context = CreateMockHubCallerContext("denied-token");
            context.Setup(c => c.ConnectionAborted).Returns(connectionAborted);
            var hub = CreateTestHub(context, webhook);
            hub.Clients = clients.Object;
            return (hub, context, caller);
        }

        [Fact]
        public async Task OnConnectedAsync_WebhookDenies_DoesNotCallStrategy()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
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
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
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
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(true);

            var mockContext = CreateMockHubCallerContext(bearerToken: token);
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            webhook.Verify(w => w.AuthorizePluginAsync(
                It.IsAny<string>(),
                token,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_NoToken_PassesNullTokenToWebhook()
        {
            var webhook = new Mock<IAuthorizationWebhookService>();
            webhook.Setup(x => x.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync(true);

            var mockContext = CreateMockHubCallerContext(bearerToken: null);
            var hub = CreateTestHub(mockContext, webhook);

            await hub.OnConnectedAsync();

            webhook.Verify(w => w.AuthorizePluginAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
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

        // --- auth-mode hub credential-source tests (oauth header-only, none query fallback) ---

        /// <summary>
        /// Helper: build a hub fully wired for the auth-mode tests — configurable strategy.AuthOption
        /// and webhook stubbing, mock IHubCallerClients with a settable Caller so tests can assert
        /// ForceDisconnect was invoked.
        /// </summary>
        static (TestHub Hub, Mock<IMcpConnectionStrategy> Strategy, Mock<IAuthorizationWebhookService> Webhook, Mock<IClientDisconnectable> Caller) CreateAuthModeTestHub(
            Mock<HubCallerContext> mockContext,
            Consts.MCP.Server.AuthOption authOption = Consts.MCP.Server.AuthOption.none,
            bool stubWebhookSuccess = true)
        {
            var logger = new Mock<ILogger>().Object;

            var strategy = new Mock<IMcpConnectionStrategy>();
            strategy.SetupGet(s => s.AuthOption).Returns(authOption);

            var webhook = new Mock<IAuthorizationWebhookService>();
            if (stubWebhookSuccess)
            {
                webhook.Setup(x => x.AuthorizePluginAsync(
                        It.IsAny<string>(), It.IsAny<string?>(),
                        It.IsAny<string?>(), It.IsAny<string?>(),
                        It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
                    .ReturnsAsync(true);
            }

            var caller = new Mock<IClientDisconnectable>();
            var clients = new Mock<IHubCallerClients<IClientDisconnectable>>();
            clients.SetupGet(c => c.Caller).Returns(caller.Object);

            var hub = new TestHub(logger, strategy.Object, webhook.Object);
            hub.Context = mockContext.Object;
            hub.Clients = clients.Object;
            return (hub, strategy, webhook, caller);
        }

        [Fact]
        public async Task OnConnectedAsync_AuthNone_EmptyToken_DoesNotReject()
        {
            // auth=none: empty-token connections are legitimate; the webhook still runs and the
            // connection is accepted. (The legacy auth=required empty-token short-circuit was
            // removed with the mode in mcp-authorize b5.)
            var mockContext = CreateMockHubCallerContext(bearerToken: null);
            var (hub, _, webhook, _) = CreateAuthModeTestHub(
                mockContext,
                authOption: Consts.MCP.Server.AuthOption.none);

            await hub.OnConnectedAsync();

            hub.ConnectionRejectedFlag.ShouldBeFalse();
            mockContext.Verify(c => c.Abort(), Times.Never);

            // Webhook IS invoked — auth=none keeps the existing behavior.
            webhook.Verify(w => w.AuthorizePluginAsync(
                    It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once);
        }

        // --- OAuth hub credential source: Authorization header only, no ?access_token= (b2) ---

        static Mock<HubCallerContext> CreateContextWithQueryAndHeader(string? queryAccessToken, string? bearerToken)
        {
            var mockContext = new Mock<HubCallerContext>();
            mockContext.Setup(c => c.ConnectionId).Returns(UniqueId());
            mockContext.Setup(c => c.ConnectionAborted).Returns(CancellationToken.None);

            var httpContext = new DefaultHttpContext();
            if (queryAccessToken != null)
                httpContext.Request.QueryString = new QueryString("?access_token=" + queryAccessToken);
            if (bearerToken != null)
                httpContext.Request.Headers["Authorization"] = $"Bearer {bearerToken}";

            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = httpContext });
            mockContext.Setup(c => c.Features).Returns(features);
            return mockContext;
        }

        [Fact]
        public async Task OnConnectedAsync_OAuthMode_IgnoresAccessTokenQueryParam_UsesHeader()
        {
            var mockContext = CreateContextWithQueryAndHeader(queryAccessToken: "QUERY-TOKEN", bearerToken: "HEADER-TOKEN");
            var (hub, _, webhook, _) = CreateAuthModeTestHub(mockContext, authOption: Consts.MCP.Server.AuthOption.oauth);

            await hub.OnConnectedAsync();

            // oauth mode uses the Authorization header token; the query param is ignored.
            webhook.Verify(w => w.AuthorizePluginAsync(
                    It.IsAny<string>(), "HEADER-TOKEN",
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_OAuthMode_QueryParamOnly_TokenIgnored()
        {
            var mockContext = CreateContextWithQueryAndHeader(queryAccessToken: "QUERY-TOKEN", bearerToken: null);
            var (hub, _, webhook, _) = CreateAuthModeTestHub(mockContext, authOption: Consts.MCP.Server.AuthOption.oauth);

            await hub.OnConnectedAsync();

            // No Authorization header ⇒ no token in oauth mode (query param NOT honored).
            webhook.Verify(w => w.AuthorizePluginAsync(
                    It.IsAny<string>(), null,
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task OnConnectedAsync_LegacyMode_StillHonorsAccessTokenQueryParam()
        {
            // Regression guard: legacy (auth=none) keeps the ?access_token= query fallback.
            var mockContext = CreateContextWithQueryAndHeader(queryAccessToken: "QUERY-TOKEN", bearerToken: null);
            var (hub, _, webhook, _) = CreateAuthModeTestHub(mockContext, authOption: Consts.MCP.Server.AuthOption.none);

            await hub.OnConnectedAsync();

            webhook.Verify(w => w.AuthorizePluginAsync(
                    It.IsAny<string>(), "QUERY-TOKEN",
                    It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
                Times.Once);
        }
    }
}
