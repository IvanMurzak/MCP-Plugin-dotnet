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
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// A real loopback Kestrel host running the REAL <c>WithMcpServer</c> + <c>WithMcpPluginServer</c>
    /// wiring over the REAL Streamable HTTP transport in <c>auth=none</c> mode, plus the JSON-RPC
    /// plumbing needed to drive an MCP session against it.
    ///
    /// <para>This is how a test reaches a router the way production does. Reaching one directly is
    /// not practical — the handlers take a <c>RequestContext&lt;T&gt;</c> that only the MCP server
    /// constructs — so an end-to-end host is the seam, and every list-degradation test needs the
    /// same one.</para>
    ///
    /// <para>Note that <c>auth=none</c> is passed EXPLICITLY. <c>DataArguments</c> also reads the
    /// environment, and a worktree's <c>.worktree.env</c> sets <c>MCP_AUTHORIZATION=required</c>;
    /// without the explicit argument the host would come up in a different mode.</para>
    /// </summary>
    public sealed class NoneAuthMcpHost : IAsyncDisposable
    {
        readonly WebApplication _app;
        readonly string _baseUrl;

        NoneAuthMcpHost(WebApplication app, string baseUrl)
        {
            _app = app;
            _baseUrl = baseUrl;
        }

        /// <summary>
        /// The loopback base URL Kestrel actually bound, e.g. <c>http://127.0.0.1:54321</c>.
        ///
        /// <para>Exposed because a test that drives this host from OUTSIDE the test process - by
        /// spawning a real plugin subprocess and pointing it at the SignalR hub
        /// (<c>NullEngineRealTransportTests</c>) - has no other way to learn the OS-assigned port.
        /// In-process callers do not need it; they go through <see cref="PostAsync"/>.</para>
        /// </summary>
        public string BaseUrl => _baseUrl;

        /// <summary>
        /// Starts the host. <paramref name="registerFakes"/> runs AFTER the production wiring, so a
        /// singleton it registers is the one the routers resolve (same pattern as
        /// <c>AmbientSessionIdOverHttpTests</c>' <c>FakeJwksKeyProvider</c>).
        /// </summary>
        public static async Task<NoneAuthMcpHost> StartAsync(Action<IServiceCollection>? registerFakes = null)
        {
            var dataArguments = new DataArguments(new[]
            {
                "client-transport=streamableHttp",
                "auth=none",
            });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            registerFakes?.Invoke(builder.Services);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();

            // Read the port the OS actually bound, rather than probing a free one and re-binding it
            // later: that probe-then-rebind leaves a window for another process to take the port.
            // Same pattern as PinnedPathRoutingTests.StartHostAsync.
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();

            return new NoneAuthMcpHost(app, address);
        }

        /// <summary>
        /// initialize + notifications/initialized; returns the server-minted session id.
        ///
        /// <para>The two requests take SEPARATE header bags on purpose. Which request carried a
        /// header is the discriminating variable for every session-semantics test in this suite, so
        /// a harness that could only set headers "for the handshake" would collapse exactly the
        /// distinction under test.</para>
        /// </summary>
        public async Task<string> HandshakeAsync(
            HttpClient client,
            string clientName = "mcp-plugin-server-test",
            IReadOnlyDictionary<string, string>? initializeHeaders = null,
            IReadOnlyDictionary<string, string>? initializedHeaders = null)
        {
            var (_, sessionId) = await PostAsync(client, null, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = clientName, version = "1.0.0" }
                }
            }, initializeHeaders);

            sessionId.ShouldNotBeNullOrEmpty("the server must mint an Mcp-Session-Id on initialize");
            await PostAsync(client, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" }, initializedHeaders);
            return sessionId!;
        }

        /// <summary>
        /// Sends one parameterless JSON-RPC request on an established session and returns the
        /// response body with the SSE framing removed.
        /// </summary>
        public async Task<string> CallAsync(
            HttpClient client,
            string sessionId,
            string method,
            int id = 2,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            var (body, _) = await PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id,
                method
            }, headers);
            return Unwrap(body);
        }

        public async Task<(string Body, string? SessionId)> PostAsync(
            HttpClient client,
            string? sessionId,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            if (headers != null)
            {
                foreach (var header in headers)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            // Assert SUCCESS rather than merely "not 401": a routing or wiring regression answering
            // 404 / 405 / 500 would otherwise resurface downstream as a JsonDocument.Parse failure,
            // which reads as a broken harness instead of the regression it is.
            response.IsSuccessStatusCode.ShouldBeTrue(
                $"POST /mcp must succeed in auth=none mode; status {(int)response.StatusCode} {response.StatusCode}, body: {body}");
            var issued = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
            return (body, issued);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        /// <summary>
        /// The streamable-HTTP reply is an SSE frame; pull the JSON out of its <c>data:</c> lines.
        ///
        /// <para>This assumes ONE JSON document per reply: the <c>data:</c> payloads are concatenated
        /// with no separator, so a reply carrying a second event — a notification alongside the
        /// response, or a multi-line data payload — comes back as two documents run together and
        /// fails to parse. Every caller today sends one request and reads one response. A caller that
        /// needs more should split the frames here rather than work around the concatenation.</para>
        ///
        /// <para>Public because <see cref="CallAsync"/> only sends PARAMETERLESS requests: a caller
        /// that needs <c>params</c> (a <c>tools/call</c>, a <c>resources/read</c>) posts through
        /// <see cref="PostAsync"/> and needs the same unwrapping. Duplicating it in the caller would
        /// fork the "one JSON document per reply" assumption documented above.</para>
        /// </summary>
        public static string Unwrap(string body)
        {
            if (!body.Contains("data:", StringComparison.Ordinal))
                return body;
            var sb = new StringBuilder();
            foreach (var line in body.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("data:", StringComparison.Ordinal))
                    sb.Append(trimmed.Substring(5).Trim());
            }
            return sb.Length > 0 ? sb.ToString() : body;
        }
    }
}
