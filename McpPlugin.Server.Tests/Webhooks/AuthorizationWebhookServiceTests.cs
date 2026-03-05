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
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Models;
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
    public class AuthorizationWebhookServiceTests
    {
        const string WebhookUrl = "https://example.com/authorize";

        Mock<IHttpClientFactory> CreateMockHttpClientFactory(HttpResponseMessage response)
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook"))
                .Returns(httpClient);

            return factory;
        }

        WebhookOptions CreateOptions(bool authorizationEnabled = true, bool failOpen = false)
        {
            return new WebhookOptions(
                toolWebhookUrl: null,
                promptWebhookUrl: null,
                resourceWebhookUrl: null,
                connectionWebhookUrl: null,
                tokenValue: null,
                headerName: null,
                timeoutMs: 10000,
                authorizationWebhookUrl: authorizationEnabled ? WebhookUrl : null,
                authorizationFailOpen: failOpen);
        }

        ILogger<AuthorizationWebhookService> CreateMockLogger()
        {
            return new Mock<ILogger<AuthorizationWebhookService>>().Object;
        }

        [Fact]
        public async Task AuthorizeAiAgentAsync_WebhookAllows_ReturnsTrue()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new AuthorizationResponse { Allowed = true }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: "192.168.1.1",
                userAgent: "TestAgent/1.0",
                requestPath: "/api/test",
                cancellationToken: CancellationToken.None);

            result.ShouldBeTrue();
            response.Dispose();
        }

        [Fact]
        public async Task AuthorizeAiAgentAsync_WebhookDenies_ReturnsFalse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new AuthorizationResponse { Allowed = false, Reason = "Access denied" }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: "192.168.1.1",
                userAgent: "TestAgent/1.0",
                requestPath: "/api/test");

            result.ShouldBeFalse();
            response.Dispose();
        }

        [Fact]
        public async Task AuthorizePluginAsync_WebhookAllows_ReturnsTrue()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new AuthorizationResponse { Allowed = true }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizePluginAsync(
                connectionId: "conn456",
                bearerToken: "plugin-token",
                clientName: "MyPlugin",
                clientVersion: "1.0.0");

            result.ShouldBeTrue();
            response.Dispose();
        }

        [Fact]
        public async Task AuthorizePluginAsync_WebhookDenies_ReturnsFalse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new AuthorizationResponse { Allowed = false }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizePluginAsync(
                connectionId: "conn456",
                bearerToken: "plugin-token",
                clientName: "MyPlugin",
                clientVersion: "1.0.0");

            result.ShouldBeFalse();
            response.Dispose();
        }

        [Fact]
        public async Task NonSuccessStatusCode_FailOpenFalse_ReturnsFalse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions(failOpen: false);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeFalse();
            response.Dispose();
        }

        [Fact]
        public async Task NonSuccessStatusCode_FailOpenTrue_ReturnsTrue()
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions(failOpen: true);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeTrue();
            response.Dispose();
        }

        [Fact]
        public async Task InvalidJsonResponse_FailOpenFalse_ReturnsFalse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ invalid json", System.Text.Encoding.UTF8, "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions(failOpen: false);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeFalse();
            response.Dispose();
        }

        [Fact]
        public async Task InvalidJsonResponse_FailOpenTrue_ReturnsTrue()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ invalid json", System.Text.Encoding.UTF8, "application/json")
            };

            var factory = CreateMockHttpClientFactory(response);
            var options = CreateOptions(failOpen: true);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeTrue();
            response.Dispose();
        }

        [Fact]
        public async Task TimeoutOccurs_FailOpenFalse_ReturnsFalse()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
                {
                    // Simulate timeout
                    await Task.Delay(100, ct);
                    throw new OperationCanceledException();
                });

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = new WebhookOptions(
                toolWebhookUrl: null,
                promptWebhookUrl: null,
                resourceWebhookUrl: null,
                connectionWebhookUrl: null,
                tokenValue: null,
                headerName: null,
                timeoutMs: 50,  // Very short timeout
                authorizationWebhookUrl: WebhookUrl,
                authorizationFailOpen: false);

            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeFalse();
        }

        [Fact]
        public async Task TimeoutOccurs_FailOpenTrue_ReturnsTrue()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
                {
                    await Task.Delay(100, ct);
                    throw new OperationCanceledException();
                });

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = new WebhookOptions(
                toolWebhookUrl: null,
                promptWebhookUrl: null,
                resourceWebhookUrl: null,
                connectionWebhookUrl: null,
                tokenValue: null,
                headerName: null,
                timeoutMs: 50,
                authorizationWebhookUrl: WebhookUrl,
                authorizationFailOpen: true);

            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeTrue();
        }

        [Fact]
        public async Task NetworkException_FailOpenFalse_ReturnsFalse()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Throws(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = CreateOptions(failOpen: false);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeFalse();
        }

        [Fact]
        public async Task NetworkException_FailOpenTrue_ReturnsTrue()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Throws(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = CreateOptions(failOpen: true);
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            var result = await service.AuthorizeAiAgentAsync(
                connectionId: "conn123",
                bearerToken: "token123",
                remoteIpAddress: null,
                userAgent: null,
                requestPath: null);

            result.ShouldBeTrue();
        }

        [Fact]
        public async Task AuthorizeAiAgentAsync_IncludesCorrectEventType()
        {
            string? capturedBody = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
                {
                    capturedBody = await req.Content.ReadAsStringAsync(ct);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new AuthorizationResponse { Allowed = true }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                });

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            await service.AuthorizeAiAgentAsync("conn123", "token", null, null, null);

            capturedBody.ShouldNotBeNull();
            var request = JsonSerializer.Deserialize<AuthorizationRequest>(capturedBody!);
            request!.EventType.ShouldBe("authorization.ai-agent");
            request.ClientType.ShouldBe("ai-agent");
        }

        [Fact]
        public async Task AuthorizePluginAsync_IncludesCorrectEventType()
        {
            string? capturedBody = null;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
                {
                    capturedBody = await req.Content.ReadAsStringAsync(ct);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new AuthorizationResponse { Allowed = true }),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                });

            var httpClient = new HttpClient(handler.Object);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient("webhook")).Returns(httpClient);

            var options = CreateOptions();
            var service = new AuthorizationWebhookService(options, factory.Object, CreateMockLogger());

            await service.AuthorizePluginAsync("conn123", "token", "PluginName", "1.0.0");

            capturedBody.ShouldNotBeNull();
            var request = JsonSerializer.Deserialize<AuthorizationRequest>(capturedBody!);
            request!.EventType.ShouldBe("authorization.plugin");
            request.ClientType.ShouldBe("plugin");
        }
    }
}
