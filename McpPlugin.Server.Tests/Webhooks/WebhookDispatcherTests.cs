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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace McpPlugin.Server.Tests.Webhooks
{
    [Collection("McpPlugin.Server")]
    public class WebhookDispatcherTests
    {
        static WebhookOptions CreateOptions(string toolUrl = "https://example.com/hooks")
        {
            return new WebhookOptions(toolUrl, null, null, null, "test-token", "X-Webhook-Token", 10000);
        }

        [Fact]
        public void TryEnqueue_WithValidMessage_ReturnsTrue()
        {
            var logger = Mock.Of<ILogger<WebhookDispatcher>>();
            var httpFactory = Mock.Of<IHttpClientFactory>();
            var options = CreateOptions();

            var dispatcher = new WebhookDispatcher(logger, httpFactory, options);
            var message = new WebhookMessage("https://example.com/hooks", "{}", "X-Webhook-Token", "test-token");

            var result = dispatcher.TryEnqueue(message);

            result.ShouldBeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_DeliversHttpPost_WithTokenHeader()
        {
            var logger = Mock.Of<ILogger<WebhookDispatcher>>();
            var options = CreateOptions();

            HttpRequestMessage? capturedRequest = null;
            var processed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new MockHttpMessageHandler(req =>
            {
                capturedRequest = req;
                processed.TrySetResult(true);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient("webhook")).Returns(() => new HttpClient(handler, disposeHandler: false));

            var dispatcher = new WebhookDispatcher(logger, httpFactory.Object, options);
            var message = new WebhookMessage("https://example.com/hooks", "{\"test\":true}", "X-Webhook-Token", "test-token");

            dispatcher.TryEnqueue(message);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.StartAsync(cts.Token);

            await processed.Task.WaitAsync(cts.Token);

            await dispatcher.StopAsync(cts.Token);

            capturedRequest.ShouldNotBeNull();
            capturedRequest!.Method.ShouldBe(HttpMethod.Post);
            capturedRequest.RequestUri!.ToString().ShouldBe("https://example.com/hooks");
            capturedRequest.Headers.TryGetValues("X-Webhook-Token", out var tokenValues).ShouldBeTrue();
        }

        [Fact]
        public async Task ExecuteAsync_WhenNoToken_OmitsHeader()
        {
            var logger = Mock.Of<ILogger<WebhookDispatcher>>();
            var options = new WebhookOptions("https://example.com/hooks", null, null, null, null, null, 10000);

            HttpRequestMessage? capturedRequest = null;
            var processed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new MockHttpMessageHandler(req =>
            {
                capturedRequest = req;
                processed.TrySetResult(true);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient("webhook")).Returns(() => new HttpClient(handler, disposeHandler: false));

            var dispatcher = new WebhookDispatcher(logger, httpFactory.Object, options);
            var message = new WebhookMessage("https://example.com/hooks", "{}", null, null);

            dispatcher.TryEnqueue(message);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.StartAsync(cts.Token);

            await processed.Task.WaitAsync(cts.Token);

            await dispatcher.StopAsync(cts.Token);

            capturedRequest.ShouldNotBeNull();
            capturedRequest!.Headers.TryGetValues("X-Webhook-Token", out _).ShouldBeFalse();
        }

        [Fact]
        public async Task ExecuteAsync_WhenHttpFails_LogsErrorAndContinues()
        {
            var mockLogger = new Mock<ILogger<WebhookDispatcher>>();
            var options = CreateOptions();

            var callCount = 0;
            var allProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new MockHttpMessageHandler(req =>
            {
                var current = Interlocked.Increment(ref callCount);
                if (current == 1)
                    throw new HttpRequestException("Connection refused");
                allProcessed.TrySetResult(true);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient("webhook")).Returns(() => new HttpClient(handler, disposeHandler: false));

            var dispatcher = new WebhookDispatcher(mockLogger.Object, httpFactory.Object, options);

            dispatcher.TryEnqueue(new WebhookMessage("https://example.com/hooks", "{\"first\":true}", null, null));
            dispatcher.TryEnqueue(new WebhookMessage("https://example.com/hooks", "{\"second\":true}", null, null));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await dispatcher.StartAsync(cts.Token);

            await allProcessed.Task.WaitAsync(cts.Token);

            await dispatcher.StopAsync(cts.Token);

            callCount.ShouldBe(2);
        }

        [Fact]
        public async Task ExecuteAsync_WhenRequestExceedsTimeout_CancelsAndContinues()
        {
            var mockLogger = new Mock<ILogger<WebhookDispatcher>>();
            var options = new WebhookOptions("https://example.com/hooks", null, null, null, null, null, 500);

            var callCount = 0;
            var timeoutTokenWasCancelled = false;
            var secondProcessed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var handler = new AsyncMockHttpMessageHandler(async (req, ct) =>
            {
                var current = Interlocked.Increment(ref callCount);
                if (current == 1)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        timeoutTokenWasCancelled = true;
                        throw;
                    }
                }
                secondProcessed.TrySetResult(true);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var httpFactory = new Mock<IHttpClientFactory>();
            httpFactory.Setup(f => f.CreateClient("webhook")).Returns(() => new HttpClient(handler, disposeHandler: false));

            var dispatcher = new WebhookDispatcher(mockLogger.Object, httpFactory.Object, options);

            dispatcher.TryEnqueue(new WebhookMessage("https://example.com/hooks", "{\"slow\":true}", null, null));
            dispatcher.TryEnqueue(new WebhookMessage("https://example.com/hooks", "{\"fast\":true}", null, null));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await dispatcher.StartAsync(cts.Token);

            await secondProcessed.Task.WaitAsync(cts.Token);

            await dispatcher.StopAsync(cts.Token);

            callCount.ShouldBe(2);
            timeoutTokenWasCancelled.ShouldBeTrue();
        }

        sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_handler(request));
            }
        }

        sealed class AsyncMockHttpMessageHandler : HttpMessageHandler
        {
            readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

            public AsyncMockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _handler(request, cancellationToken);
            }
        }
    }
}
