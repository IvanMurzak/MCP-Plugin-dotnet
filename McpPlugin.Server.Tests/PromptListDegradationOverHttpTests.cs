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
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end cover for what a client actually receives from <c>prompts/list</c> when the
    /// plugin-side call fails — the routine "the engine plugin has not attached yet" window.
    ///
    /// <para>The defect: <see cref="ExtensionsPrompt.SetError(ModelContextProtocol.Protocol.ListPromptsResult, string)"/>
    /// used to fabricate a single synthetic prompt named <c>"Error"</c> whose description carried the
    /// internal failure text. It never threw, so nothing in the suite went red — but on the wire the
    /// client saw a REAL, selectable prompt, with internal .NET type names in its description. The
    /// fix returns an EMPTY catalog instead, matching the tool and resource list paths; the failure
    /// text stays on the server-side log at Warn (<c>PromptRouter.List</c>), so operators lose
    /// nothing.</para>
    ///
    /// <para>The two cases are one fixture family on purpose. The failure case asserts an EMPTY
    /// catalog, and an emptiness assertion is satisfied for free by any path that never ran at all —
    /// so it is paired with <see cref="PromptsList_WhenThePluginReturnsPrompts_EmitsThemOverTheWire"/>,
    /// which drives the SAME host and the SAME seam and pins the exact prompt names that come back.
    /// The failure case additionally pins <see cref="FakePromptHub.ListCallCount"/>, so the empty
    /// catalog is provably the DEGRADATION of a call that reached the plugin seam and failed there,
    /// not the silence of a request that never arrived.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PromptListDegradationOverHttpTests
    {
        /// <summary>
        /// The regression. With the plugin call failing, <c>prompts/list</c> answers with an empty
        /// catalog and NO synthetic entry — in particular nothing named <c>"Error"</c>.
        /// </summary>
        [Fact]
        public async Task PromptsList_WhenThePluginCallFails_ReturnsEmptyCatalogWithNoSyntheticErrorPrompt()
        {
            var hub = FakePromptHub.ThatFailsWith(
                "Invoke 'RunListPrompts': Failed to invoke " +
                "'com.IvanMurzak.McpPlugin.Common.Model.RequestListPrompts' after 10 retries.");

            await using var host = await StartNoneAuthHostAsync(hub);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client);
            var body = await host.ListPromptsAsync(client, sessionId);

            using var doc = JsonDocument.Parse(body);

            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                $"prompts/list must not fail the JSON-RPC call when the plugin call fails; got: {body}");

            doc.RootElement.GetProperty("result").TryGetProperty("prompts", out var prompts).ShouldBeTrue(
                $"the degraded catalog is empty, not absent; got: {body}");

            var names = prompts.EnumerateArray()
                .Select(p => p.TryGetProperty("name", out var n) ? n.GetString() : null)
                .ToList();

            // Asserted as an exact set rather than "does not contain Error": an entry named anything
            // else would be just as much a fabrication, and an exact-set assertion cannot be satisfied
            // by a differently-named synthetic element.
            names.ShouldBeEmpty($"the failed prompts/list must emit no prompt at all; got: {body}");

            // Positive artifact: the call REACHED the plugin seam and failed there. Without this, the
            // emptiness above would also hold for a request that never got as far as the router.
            hub.ListCallCount.ShouldBe(1, "the router must have called the plugin hub exactly once");
        }

        /// <summary>
        /// The paired positive case: the same host, the same seam, a SUCCESSFUL plugin response — and
        /// the prompts appear on the wire. This is what makes the empty-catalog assertion above
        /// meaningful: it proves this fixture is capable of emitting prompts in the first place.
        /// </summary>
        [Fact]
        public async Task PromptsList_WhenThePluginReturnsPrompts_EmitsThemOverTheWire()
        {
            var hub = FakePromptHub.ThatReturns(
                new ResponsePrompt { Name = "alpha", Description = "first", Enabled = true },
                new ResponsePrompt { Name = "beta", Description = "second", Enabled = true });

            await using var host = await StartNoneAuthHostAsync(hub);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client);
            var body = await host.ListPromptsAsync(client, sessionId);

            using var doc = JsonDocument.Parse(body);

            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                $"prompts/list must succeed when the plugin answers; got: {body}");

            var names = doc.RootElement.GetProperty("result").GetProperty("prompts")
                .EnumerateArray()
                .Select(p => p.GetProperty("name").GetString())
                .ToList();

            names.ShouldBe(new List<string?> { "alpha", "beta" },
                $"the plugin's prompts must reach the client verbatim and in order; got: {body}");

            hub.ListCallCount.ShouldBe(1, "the router must have called the plugin hub exactly once");
        }

        // ───────────────────────────── fake plugin hub ─────────────────────────────

        /// <summary>
        /// Stands in for <see cref="RemotePromptRunner"/> so both outcomes of the plugin call are
        /// reachable without a live SignalR plugin: the real runner needs an attached engine plugin,
        /// and its failure path costs a full retry budget in wall-clock time.
        /// </summary>
        sealed class FakePromptHub : IClientPromptHub
        {
            ResponseData<ResponseListPrompts>? _response;
            int _listCallCount;

            public int ListCallCount => Volatile.Read(ref _listCallCount);

            public static FakePromptHub ThatFailsWith(string message) => new FakePromptHub
            {
                _response = ResponseData<ResponseListPrompts>.Error("req-fail", message)
            };

            public static FakePromptHub ThatReturns(params ResponsePrompt[] prompts)
            {
                var response = ResponseData<ResponseListPrompts>.Success("req-ok");
                response.Value = new ResponseListPrompts { Prompts = prompts.ToList() };
                return new FakePromptHub { _response = response };
            }

            public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _listCallCount);
                return Task.FromResult(_response!);
            }

            public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
                => throw new NotSupportedException("These tests only drive prompts/list.");
        }

        // ───────────────────────────── real loopback none-auth host ─────────────────────────────

        static async Task<NoneAuthHost> StartNoneAuthHostAsync(IClientPromptHub promptHub)
        {
            var port = FreeLoopbackPort();

            var dataArguments = new DataArguments(new[]
            {
                $"port={port}",
                "client-transport=streamableHttp",
                "auth=none",
            });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            // Registered AFTER the production wiring so this singleton is the one the router resolves
            // (same pattern as AmbientSessionIdOverHttpTests' FakeJwksKeyProvider).
            builder.Services.AddSingleton(promptHub);

            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();

            return new NoneAuthHost(app, $"http://127.0.0.1:{port}");
        }

        sealed class NoneAuthHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            readonly string _baseUrl;

            public NoneAuthHost(WebApplication app, string baseUrl)
            {
                _app = app;
                _baseUrl = baseUrl;
            }

            /// <summary>initialize + notifications/initialized; returns the server-minted session id.</summary>
            public async Task<string> HandshakeAsync(HttpClient client)
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
                        clientInfo = new { name = "prompt-list-degradation-test", version = "1.0.0" }
                    }
                });

                sessionId.ShouldNotBeNullOrEmpty("the server must mint an Mcp-Session-Id on initialize");
                await PostAsync(client, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" });
                return sessionId!;
            }

            public async Task<string> ListPromptsAsync(HttpClient client, string sessionId)
            {
                var (body, _) = await PostAsync(client, sessionId, new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "prompts/list"
                });
                return Unwrap(body);
            }

            async Task<(string Body, string? SessionId)> PostAsync(HttpClient client, string? sessionId, object payload)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (!string.IsNullOrEmpty(sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
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
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>The streamable-HTTP reply is an SSE frame; pull the JSON out of its data: lines.</summary>
        static string Unwrap(string body)
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
