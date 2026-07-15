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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The MCP streamable-HTTP endpoint must be SERVED at the project-pinned config paths
    /// <c>/p/&lt;pin&gt;</c> (the nginx-stripped form) and <c>/mcp/p/&lt;pin&gt;</c> (the direct form),
    /// not only at <c>/</c> and <c>/mcp</c> (mcp-authorize g4, design 04 D14 / 03 Flow F). Before the
    /// fix a pinned request 404'd before the pin-capture middleware or the OAuth 401 challenge could
    /// run — the endpoint mapped at only two literal paths. These tests drive REAL loopback hosts (the
    /// full production wiring via <c>WithMcpServer</c> + <c>WithMcpPluginServer</c> + <c>UseMcpPluginServer</c>)
    /// to prove the pinned paths now route to the SAME handler as <c>/mcp</c> in both auth modes, and a
    /// companion middleware test proves the pin is captured live from the pinned request path.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PinnedPathRoutingTests
    {
        const string Pin = "aabbccdd";

        // ───────────────── none mode: pinned paths establish an anonymous session, like /mcp ─────────────────

        [Fact]
        public async Task NoneMode_PinnedPaths_ReachMcpHandler_LikeBareMcp()
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.none);
            using var client = NewClient(host);

            // Baseline: a proper initialize on /mcp establishes a session (200 + Mcp-Session-Id header).
            await AssertSessionEstablishedAsync(client, "/mcp");
            // The g4 fix: the pinned paths must behave identically (they 404'd before the fix).
            await AssertSessionEstablishedAsync(client, $"/p/{Pin}");
            await AssertSessionEstablishedAsync(client, $"/mcp/p/{Pin}");

            // Control: a genuinely unmapped path still 404s — the pinned paths match by mapping, not by accident.
            using var unmapped = await client.SendAsync(InitializeRequest("/definitely-not-mapped"), HttpCompletionOption.ResponseHeadersRead);
            unmapped.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // ───────────────── oauth mode: pinned paths carry the same 401 gate as /mcp ─────────────────

        [Fact]
        public async Task OAuthMode_PinnedPaths_RequireAuthorization_Return401()
        {
            await using var host = await StartHostAsync(Consts.MCP.Server.AuthOption.oauth);
            using var client = NewClient(host);

            // Anonymous (no bearer): /mcp challenges with 401. The pinned paths must too — a 401 proves
            // the route is BOTH mapped and gated (a 404 would mean it was never mapped, the pre-fix bug).
            foreach (var path in new[] { "/mcp", $"/p/{Pin}", $"/mcp/p/{Pin}" })
            {
                using var resp = await client.SendAsync(InitializeRequest(path), HttpCompletionOption.ResponseHeadersRead);
                resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{path} must be mapped AND gated (401), not 404");
            }

            // Control: an unmapped path 404s even in oauth mode — distinguishes mapped-and-gated from unmapped.
            using var unmapped = await client.SendAsync(InitializeRequest("/definitely-not-mapped"), HttpCompletionOption.ResponseHeadersRead);
            unmapped.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // ───────────────── pin capture is live during the request on the pinned paths ─────────────────

        [Theory]
        [InlineData("/p/aabbccdd", "aabbccdd")]
        [InlineData("/mcp/p/aabbccdd", "aabbccdd")]
        [InlineData("/mcp", null)] // no /p/ segment ⇒ no pin
        public async Task Middleware_CapturesProjectPin_LiveDuringRequest(string path, string? expectedPin)
        {
            string? capturedDuringRequest = "<unset>";
            var middleware = new McpSessionTokenMiddleware(_ =>
            {
                // Observed while the request is in flight — the middleware clears it in its finally block,
                // so the endpoint handler (the real MCP session) sees exactly this value.
                capturedDuringRequest = McpSessionTokenContext.CurrentProjectPin;
                return Task.CompletedTask;
            });

            var ctx = new DefaultHttpContext { RequestServices = EmptyProvider };
            ctx.Request.Path = new PathString(path);

            await middleware.InvokeAsync(ctx);

            capturedDuringRequest.ShouldBe(expectedPin);
            // The AsyncLocal pin is reset once the request completes so it never leaks into the next one.
            McpSessionTokenContext.CurrentProjectPin.ShouldBeNull();
        }

        // ───────────────── helpers ─────────────────

        static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

        static HttpClient NewClient(RunningHost host)
            => new HttpClient { BaseAddress = new Uri(host.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };

        static async Task AssertSessionEstablishedAsync(HttpClient client, string path)
        {
            using var resp = await client.SendAsync(InitializeRequest(path), HttpCompletionOption.ResponseHeadersRead);
            resp.StatusCode.ShouldBe(HttpStatusCode.OK, $"{path} must reach the MCP handler (not 404)");
            resp.Headers.Contains("Mcp-Session-Id").ShouldBeTrue($"{path} must establish an MCP session (Mcp-Session-Id header)");
        }

        static HttpRequestMessage InitializeRequest(string path)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, path);
            // Streamable-HTTP requires the client to accept both JSON and the SSE stream.
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{" +
                "\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                "\"clientInfo\":{\"name\":\"pin-routing-test\",\"version\":\"1.0.0\"}}}",
                Encoding.UTF8, "application/json");
            return req;
        }

        static async Task<RunningHost> StartHostAsync(Consts.MCP.Server.AuthOption mode)
        {
            var args = mode == Consts.MCP.Server.AuthOption.oauth
                ? new[] { "client-transport=streamableHttp", "auth=oauth", "auth-issuer=https://as.example", "public-url=http://127.0.0.1:23999" }
                : new[] { "client-transport=streamableHttp", "auth=none" };
            var dataArguments = new DataArguments(args);

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            app.UseMcpPluginServer(dataArguments);

            await app.StartAsync();

            var address = app.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();
            return new RunningHost(app, address);
        }

        sealed class RunningHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            public string BaseUrl { get; }

            public RunningHost(WebApplication app, string baseUrl)
            {
                _app = app;
                BaseUrl = baseUrl;
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
