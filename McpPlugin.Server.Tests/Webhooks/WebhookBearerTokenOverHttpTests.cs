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
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using com.IvanMurzak.McpPlugin.Server.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Webhooks
{
    /// <summary>
    /// The <c>bearerToken</c> a tool / prompt / resource webhook carries must be the token of the
    /// request being reported, not the one the MCP session was opened with. Streamable HTTP runs
    /// every handler of a session on the ExecutionContext captured at <c>initialize</c>
    /// (<c>PerSessionExecutionContext</c>), so the ambient <c>McpSessionTokenContext.CurrentToken</c>
    /// is frozen there; a client that refreshes its 60-minute OAuth access token on the same
    /// session would otherwise keep reporting the expired token, which the backend cannot verify,
    /// and the event loses its owner.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class WebhookBearerTokenOverHttpTests
    {
        static readonly string[] WebhookArgs =
        {
            "webhook-tool-url=https://webhooks.example/tool",
            "webhook-prompt-url=https://webhooks.example/prompt",
            "webhook-resource-url=https://webhooks.example/resource",
            "webhook-token=test-webhook-secret",
        };

        sealed class CapturingDispatcher : IWebhookDispatcher
        {
            readonly List<WebhookMessage> _messages = new List<WebhookMessage>();

            public bool TryEnqueue(WebhookMessage message)
            {
                lock (_messages) _messages.Add(message);
                return true;
            }

            /// <summary>The <c>data.bearerToken</c> of the single event sent to <paramref name="url"/>.</summary>
            public string? SingleBearerTokenFor(string url)
            {
                WebhookMessage[] matching;
                lock (_messages) matching = _messages.Where(m => m.TargetUrl == url).ToArray();
                matching.Length.ShouldBe(1, $"exactly one webhook must be sent to {url}");
                using var doc = JsonDocument.Parse(matching[0].JsonPayload);
                return doc.RootElement.GetProperty("data").TryGetProperty("bearerToken", out var token)
                    ? token.GetString()
                    : null;
            }
        }

        static void RegisterFakes(IServiceCollection services, CapturingDispatcher dispatcher, AmbientCaptureToolHub tools)
        {
            services.AddSingleton<IWebhookDispatcher>(dispatcher);
            services.AddSingleton<IClientToolHub>(tools);

            var prompts = new Mock<IClientPromptHub>();
            prompts.Setup(p => p.RunGetPrompt(It.IsAny<RequestGetPrompt>()))
                .ReturnsAsync(ResponseData<ResponseGetPrompt>.Error("req", "no prompt here"));
            services.AddSingleton(prompts.Object);

            var resources = new Mock<IClientResourceHub>();
            resources.Setup(r => r.RunResourceContent(It.IsAny<RequestResourceContent>()))
                .ReturnsAsync(ResponseData<ResponseResourceContent[]>.Error("req", "no resource here"));
            services.AddSingleton(resources.Object);
        }

        static object ToolCall(int id) => new
        {
            jsonrpc = "2.0", id, method = "tools/call",
            @params = new { name = AmbientCaptureToolHub.AttributedToolName, arguments = new { } }
        };

        static object PromptGet(int id) => new
        {
            jsonrpc = "2.0", id, method = "prompts/get",
            @params = new { name = "some-prompt", arguments = new Dictionary<string, string>() }
        };

        static object ResourceRead(int id) => new
        {
            jsonrpc = "2.0", id, method = "resources/read",
            @params = new { uri = "res://some/resource" }
        };

        [Fact]
        public async Task RefreshedTokenOnTheSameSession_IsTheTokenEveryWebhookCarries()
        {
            var dispatcher = new CapturingDispatcher();
            var tools = new AmbientCaptureToolHub();
            await using var host = await OAuthMcpHost.StartAsync(
                s => RegisterFakes(s, dispatcher, tools), extraArgs: WebhookArgs);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // Same account, a different (refreshed) access token: the SDK's session/user binding
            // accepts it, which is exactly the OAuth refresh case.
            var openingToken = host.TokenForAccount(host.AccountId);
            var refreshedToken = host.TokenForAccount(host.AccountId);
            refreshedToken.ShouldNotBe(openingToken, "the test needs two distinct tokens of one account");

            var sessionId = await host.HandshakeAsync(client, "webhook-token-refresh", bearerToken: openingToken);

            await host.PostAsync(client, sessionId, ToolCall(2), bearerToken: refreshedToken);
            await host.PostAsync(client, sessionId, PromptGet(3), bearerToken: refreshedToken);
            await host.PostAsync(client, sessionId, ResourceRead(4), bearerToken: refreshedToken);

            // Control: a handler of this session still sees the OPENING token as its ambient token
            // even on a request that carries the refreshed one, so the assertions below discriminate
            // between "per-request" and "session-start". (tools/list is the seam that reaches the
            // fake hub in oauth mode without a registered engine instance.)
            await host.CallAsync(client, sessionId, "tools/list", id: 5, bearerToken: refreshedToken);
            tools.LastListSnapshot.Token.ShouldBe(openingToken,
                "the ambient token is frozen at session start; if this changes the test no longer discriminates");

            dispatcher.SingleBearerTokenFor("https://webhooks.example/tool").ShouldBe(refreshedToken);
            dispatcher.SingleBearerTokenFor("https://webhooks.example/prompt").ShouldBe(refreshedToken);
            dispatcher.SingleBearerTokenFor("https://webhooks.example/resource").ShouldBe(refreshedToken);
        }

        [Fact]
        public async Task NoAuthenticatedUser_FallsBackToTheSessionToken()
        {
            // auth=none issues no token claim, so request.User carries nothing and the collector
            // keeps reporting the ambient token RunSessionHandler took from the initialize request.
            var dispatcher = new CapturingDispatcher();
            var tools = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(
                s => RegisterFakes(s, dispatcher, tools), WebhookArgs);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            const string sessionToken = "session-opening-token";
            var bearer = new Dictionary<string, string> { ["Authorization"] = "Bearer " + sessionToken };
            var sessionId = await host.HandshakeAsync(client, "webhook-token-fallback", bearer, bearer);

            await host.PostAsync(client, sessionId, ToolCall(2), bearer);
            await host.PostAsync(client, sessionId, PromptGet(3), bearer);
            await host.PostAsync(client, sessionId, ResourceRead(4), bearer);

            dispatcher.SingleBearerTokenFor("https://webhooks.example/tool").ShouldBe(sessionToken);
            dispatcher.SingleBearerTokenFor("https://webhooks.example/prompt").ShouldBe(sessionToken);
            dispatcher.SingleBearerTokenFor("https://webhooks.example/resource").ShouldBe(sessionToken);
        }
    }
}
