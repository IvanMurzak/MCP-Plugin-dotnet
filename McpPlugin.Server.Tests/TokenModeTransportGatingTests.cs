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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end proof that offline <c>token</c> mode (mcp-authorize g6) both VALIDATES and ENFORCES
    /// the shared secret. Drives a REAL loopback host through the full production wiring
    /// (<c>WithMcpServer</c> → <c>WithMcpPluginServer</c> → <c>UseMcpPluginServer</c>): an anonymous /
    /// wrong-token request is rejected with 401 (the endpoint is <c>RequireAuthorization</c>-gated —
    /// without the gate the token would be validated but never enforced), while the correct Bearer
    /// establishes an MCP session. No RFC 9728 resource-metadata is served in token mode.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class TokenModeTransportGatingTests
    {
        const string Secret = "gating-test-shared-secret";

        [Fact]
        public async Task TokenMode_NoBearer_Returns401()
        {
            await using var host = await StartHostAsync();
            using var client = NewClient(host);

            using var resp = await client.SendAsync(InitializeRequest("/mcp", bearer: null), HttpCompletionOption.ResponseHeadersRead);
            resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "token mode must gate /mcp (401), not serve it anonymously");
        }

        [Fact]
        public async Task TokenMode_WrongBearer_Returns401()
        {
            await using var host = await StartHostAsync();
            using var client = NewClient(host);

            using var resp = await client.SendAsync(InitializeRequest("/mcp", bearer: "not-the-secret"), HttpCompletionOption.ResponseHeadersRead);
            resp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "a wrong token must be rejected");
        }

        [Fact]
        public async Task TokenMode_CorrectBearer_EstablishesSession()
        {
            await using var host = await StartHostAsync();
            using var client = NewClient(host);

            using var resp = await client.SendAsync(InitializeRequest("/mcp", bearer: Secret), HttpCompletionOption.ResponseHeadersRead);
            resp.StatusCode.ShouldBe(HttpStatusCode.OK, "the correct token must reach the MCP handler");
            resp.Headers.Contains("Mcp-Session-Id").ShouldBeTrue("a valid session must be established");
        }

        [Fact]
        public async Task TokenMode_ServesNoResourceMetadata()
        {
            await using var host = await StartHostAsync();
            using var client = NewClient(host);

            using var resp = await client.GetAsync("/.well-known/oauth-protected-resource");
            resp.StatusCode.ShouldBe(HttpStatusCode.NotFound, "token mode has no authorization server to advertise");
        }

        // ───────────────── helpers ─────────────────

        static HttpClient NewClient(RunningHost host)
            => new HttpClient { BaseAddress = new Uri(host.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };

        static HttpRequestMessage InitializeRequest(string path, string? bearer)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, path);
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (!string.IsNullOrEmpty(bearer))
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
            req.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{" +
                "\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                "\"clientInfo\":{\"name\":\"token-gating-test\",\"version\":\"1.0.0\"}}}",
                Encoding.UTF8, "application/json");
            return req;
        }

        static async Task<RunningHost> StartHostAsync()
        {
            var dataArguments = new DataArguments(new[] { "client-transport=streamableHttp", "auth=token", $"token={Secret}" });

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
