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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    [Collection("McpPlugin.Server")]
    public class AuthorizationOutageRecoveryTests
    {
        sealed class HttpContextFeature : IHttpContextFeature
        {
            public HttpContext? HttpContext { get; set; }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task PluginRegistrationDependencyOutage_AbortsUnregisteredHub_ThenFreshConnectionRegisters(bool classified)
        {
            var validator = new Mock<IOAuthTokenValidator>();
            validator.SetupSequence(x => x.ValidateAsync("same-token", TokenValidationPlane.Plugin, It.IsAny<CancellationToken>()))
                .ThrowsAsync(classified ? (Exception)new AuthorizationUnavailableException("Signing service unavailable") : new HttpRequestException("Dependency connection refused"))
                .ReturnsAsync(OAuthValidationResult.Success("jwt", "plugin-account", "mcp:plugin"));
            await using var host = await OAuthMcpHost.StartAsync(services => services.AddSingleton(validator.Object));
            var caller = new Mock<IClientMcpRpc>();
            caller.Setup(x => x.OnInitialClientData(It.IsAny<McpClientData[]>())).Returns(Task.CompletedTask);
            var clients = new Mock<IHubCallerClients<IClientMcpRpc>>();
            clients.Setup(x => x.Caller).Returns(caller.Object);
            var strategy = (AccountMcpStrategy)host.Services.GetRequiredService<IMcpConnectionStrategy>();

            Mock<HubCallerContext> ContextFor(string connectionId)
            {
                var http = new DefaultHttpContext();
                http.Request.Headers.Authorization = "Bearer same-token";
                var features = new FeatureCollection();
                features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });
                var context = new Mock<HubCallerContext>();
                context.SetupGet(x => x.Features).Returns(features);
                context.SetupGet(x => x.ConnectionId).Returns(connectionId);
                context.SetupGet(x => x.ConnectionAborted).Returns(CancellationToken.None);
                return context;
            }

            using var hub = ActivatorUtilities.CreateInstance<McpServerHub>(host.Services);
            var failedContext = ContextFor("failed-connection");
            hub.Context = failedContext.Object;
            hub.Clients = clients.Object;
            await hub.OnConnectedAsync();
            failedContext.Verify(x => x.Abort(), Times.Once);
            caller.Verify(x => x.ForceDisconnect(It.IsAny<string?>()), Times.Never);
            caller.Verify(x => x.OnInitialClientData(It.IsAny<McpClientData[]>()), Times.Never);
            strategy.Instances.Resolve("plugin-account", projectPin: null, selectedInstanceId: null).Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);

            using var recoveredHub = ActivatorUtilities.CreateInstance<McpServerHub>(host.Services);
            var recoveredContext = ContextFor("recovered-connection");
            recoveredHub.Context = recoveredContext.Object;
            recoveredHub.Clients = clients.Object;
            await recoveredHub.OnConnectedAsync();
            recoveredContext.Verify(x => x.Abort(), Times.Never);
            caller.Verify(x => x.OnInitialClientData(It.IsAny<McpClientData[]>()), Times.Once);
            strategy.Instances.Resolve("plugin-account", projectPin: null, selectedInstanceId: null).Kind.ShouldBe(InstanceResolutionKind.Resolved);
            await recoveredHub.OnDisconnectedAsync(null);
        }

        sealed class CountingToolHub : IClientToolHub, IClientSystemToolHub
        {
            public int Calls;
            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Task.FromResult(ResponseData<ResponseCallTool>.Success(request.RequestID));
            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => RunCallTool(request);
            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = Array.Empty<ResponseListTool>()
                });
            }
            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => RunListTool(request, cancellationToken);
        }

        sealed class MutableWebhook : IAuthorizationWebhookService
        {
            public bool Unavailable = true;
            public bool Allowed = true;
            Task<bool> Authorize()
            {
                if (Unavailable)
                    throw new AuthorizationUnavailableException("Backend unavailable.");
                return Task.FromResult(Allowed);
            }
            public Task<bool> AuthorizeAiAgentAsync(string connectionId, string? bearerToken,
                string? remoteIpAddress, string? userAgent, string? requestPath, CancellationToken cancellationToken = default)
                => Authorize();
            public Task<bool> AuthorizePluginAsync(string connectionId, string? bearerToken, string? clientName,
                string? clientVersion, CancellationToken cancellationToken = default, string? remoteIpAddress = null,
                string? userAgent = null, string? requestPath = null) => Authorize();
        }

        [Fact]
        public async Task JwksOutageWithoutCache_Returns503_ThenSameSignedTokenSucceeds_AndWrongSignatureIs401()
        {
            using var key = TestJwt.CreateKey();
            string? jwks = null;
            var provider = new JwksKeyProvider(_ => Task.FromResult(jwks), new InMemoryJwksDiskCache());
            var tools = new CountingToolHub();
            await using var host = await OAuthMcpHost.StartAsync(services =>
            {
                services.AddSingleton<IJwksKeyProvider>(provider);
                services.AddSingleton<IClientToolHub>(tools);
            });
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            var claims = TestJwt.Claims("https://as.example", host.BaseUrl + "/mcp", DateTimeOffset.UtcNow.AddHours(1), sub: "u1", scope: "mcp:agent");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.SignEs256(key, "key1", claims));
            using (var unavailable = await client.GetAsync("/api/tools"))
                unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            tools.Calls.ShouldBe(0);
            jwks = TestJwt.BuildJwks(key, "key1");
            using (var recovered = await client.GetAsync("/api/tools"))
                recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            tools.Calls.ShouldBe(1);
            using var wrongKey = TestJwt.CreateKey();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.SignEs256(wrongKey, "key1", claims));
            using (var rejected = await client.GetAsync("/api/tools"))
                rejected.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            tools.Calls.ShouldBe(1);
        }

        [Theory]
        [InlineData("/api/tools")]
        [InlineData("/api/system-tools")]
        public async Task ValidJwt_BackendWebhookOutage_FailsClosed503_ThenRecovers_WithSameToken(string route)
        {
            var webhook = new MutableWebhook();
            var tools = new CountingToolHub();
            await using var host = await OAuthMcpHost.StartAsync(services =>
            {
                services.AddSingleton<IAuthorizationWebhookService>(webhook);
                services.AddSingleton<IClientToolHub>(tools);
                services.AddSingleton<IClientSystemToolHub>(tools);
            });
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.TokenForAccount("recovery-account"));

            using (var unavailable = await client.GetAsync(route))
            {
                unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
                unavailable.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(5));
                unavailable.Headers.WwwAuthenticate.ShouldBeEmpty();
                (await unavailable.Content.ReadAsStringAsync()).ShouldContain("temporarily_unavailable");
            }

            tools.Calls.ShouldBe(0);
            webhook.Unavailable = false;
            using (var recovered = await client.GetAsync(route))
                recovered.StatusCode.ShouldBe(HttpStatusCode.OK, await recovered.Content.ReadAsStringAsync());
            tools.Calls.ShouldBe(1);
            webhook.Allowed = false;
            using (var denied = await client.GetAsync(route))
            {
                denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
                denied.Headers.WwwAuthenticate.ShouldNotBeEmpty();
            }
            tools.Calls.ShouldBe(1);
        }

        [Fact]
        public async Task OpaqueToken_IntrospectionOutage_Is503_NotInactive_AndRecoversWithoutNegativeCache()
        {
            string? response = null;
            var now = DateTimeOffset.UtcNow;
            var introspection = new IntrospectionClient((_, _) => Task.FromResult(response), () => now);
            var tools = new CountingToolHub();
            await using var host = await OAuthMcpHost.StartAsync(services =>
            {
                services.AddSingleton<IIntrospectionClient>(introspection);
                services.AddSingleton<IClientToolHub>(tools);
            });
            host.Services.GetRequiredService<IIntrospectionClient>().ShouldBeSameAs(introspection);
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "agd_pat_same_token");

            using (var unavailable = await client.GetAsync("/api/tools"))
            {
                unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
                unavailable.Headers.WwwAuthenticate.ShouldBeEmpty();
            }
            tools.Calls.ShouldBe(0);
            response = "{\"active\":true,\"sub\":\"u1\",\"scope\":\"mcp:agent\"}";
            using (var recovered = await client.GetAsync("/api/tools"))
                recovered.StatusCode.ShouldBe(HttpStatusCode.OK, await recovered.Content.ReadAsStringAsync());
            now = now.AddSeconds(61);
            response = "{\"active\":false}";
            using (var denied = await client.GetAsync("/api/tools"))
                denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }
}
