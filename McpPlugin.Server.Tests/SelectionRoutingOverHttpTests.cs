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
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end regression cover for sticky engine-instance routing (issue #195).
    ///
    /// <para>These tests drive a REAL loopback Kestrel host running the REAL <c>WithMcpServer</c> +
    /// <c>WithMcpPluginServer</c> oauth wiring over the REAL Streamable HTTP transport, and reach the
    /// routing decision the only way production does: an actual MCP <c>initialize</c> handshake, an
    /// actual <c>select_engine_instance</c> call, and then an actual proxied <c>tools/call</c> whose
    /// routing is observed. Every other selection test in this suite hand-builds a
    /// <see cref="SelectionToolContext"/>, and a hand-built context CANNOT observe this defect by
    /// construction — the suite was fully green while the selection had no effect whatsoever.</para>
    ///
    /// <para><b>The defect.</b> <c>HttpServerTransportOptions.PerSessionExecutionContext</c> is
    /// <c>true</c>, so a session's request handlers run on the <see cref="System.Threading.ExecutionContext"/>
    /// captured once inside <c>RunSessionHandler</c>. <c>McpSessionTokenMiddleware</c> reloads the
    /// sticky selection into <c>McpSessionTokenContext.CurrentSelectedInstanceId</c> per REQUEST, so
    /// that write never reaches an MCP handler: the sticky term of
    /// <see cref="AccountInstances.Resolve"/> was permanently null on this path and routing silently
    /// fell through to <c>single → MRU</c>. <c>select_engine_instance</c> answered "Selected …" and
    /// changed nothing.</para>
    ///
    /// <para><b>Why TWO instances.</b> With a single registered instance the <c>single</c> auto-pair
    /// rung of <see cref="AccountInstances.Resolve"/> routes to it regardless of the selection, so a
    /// one-instance test cannot tell honoured-selection from accidental-routing. Both tests below
    /// therefore register two instances and deliberately select the one that is NOT most-recently
    /// active, which is the only arrangement in which the sticky rung and the MRU rung disagree.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class SelectionRoutingOverHttpTests
    {
        const string Kid = "selection-routing-kid";
        const string Account = "acct-selection-routing";

        const string InstanceA = "instance-alpha";
        const string ProjectA = "AlphaProject";
        const string HashA = "aaaaaaaa1111222233334444555566667777888899990000aaaabbbbccccdddd0";

        const string InstanceB = "instance-beta";
        const string ProjectB = "BetaProject";
        const string HashB = "bbbbbbbb1111222233334444555566667777888899990000aaaabbbbccccdddd0";

        /// <summary>
        /// The regression: after <c>select_engine_instance</c> picks A, the NEXT tool call on the same
        /// MCP session is routed to A — even though B is the most-recently-active instance and would
        /// win the MRU rung.
        ///
        /// <para>The observed artifact is POSITIVE and is produced by the routing code itself:
        /// <see cref="AccountMcpStrategy.ResolveConnectionId"/> calls
        /// <see cref="AccountInstances.TouchByConnection"/> on the instance it resolved, so the
        /// resolved instance — and only the resolved instance — has its <c>LastActiveAt</c> moved
        /// forward by the proxied call. Asserting that A's timestamp ADVANCED (rather than asserting
        /// that B's did not) means a call that never reached routing at all cannot satisfy it.</para>
        /// </summary>
        [Fact]
        public async Task SelectedInstance_IsRoutedTo_OnSubsequentToolCall()
        {
            await using var host = await StartOAuthHostAsync();
            host.RegisterInstance(InstanceA, "unity", ProjectA, HashA, "MACHINE-A");
            await Task.Delay(50);
            host.RegisterInstance(InstanceB, "unity", ProjectB, HashB, "MACHINE-B");

            // Self-validating setup: with NO selection the account resolves to B (the MRU rung). If this
            // ever stops holding, the test below would pass for the wrong reason, so pin it here.
            host.ResolveWithoutSelection().ShouldBe(InstanceB,
                "setup precondition: B must be the most-recently-active instance, so that routing to A " +
                "can only be explained by the selection being honoured");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var sessionId = await host.InitializeAsync(client);

            var select = await host.CallToolAsync(client, sessionId, ServerNativeTools.SelectInstance,
                new { instance_id = InstanceA });
            select.IsError.ShouldBeFalse($"select_engine_instance must succeed; got: {select.Text}");

            var beforeA = host.LastActiveAt(InstanceA);
            var beforeB = host.LastActiveAt(InstanceB);

            var proxied = await host.CallToolAsync(client, sessionId, "any_engine_tool", new { });

            // The call cannot succeed (no live SignalR plugin backs the registered connection ids), but
            // it MUST have got past resolution — an unresolved session short-circuits in ToolRouter.Call
            // with the agent-actionable text below and never reaches ResolveConnectionId at all.
            proxied.Text.ShouldNotContain("No engine instances are connected",
                Case.Sensitive, "the account has two live instances, so resolution must not fail");

            var afterA = host.LastActiveAt(InstanceA);
            var afterB = host.LastActiveAt(InstanceB);

            afterA.ShouldBeGreaterThan(beforeA,
                $"the selected instance {InstanceA} must be the one routing resolved, and routing bumps " +
                $"its LastActiveAt; A {beforeA:O} -> {afterA:O}, B {beforeB:O} -> {afterB:O}. " +
                $"Tool result was: {proxied.Text}");
            afterB.ShouldBe(beforeB,
                $"the unselected instance {InstanceB} must NOT have been routed to; " +
                $"B {beforeB:O} -> {afterB:O}");
        }

        /// <summary>
        /// The re-selection half: selecting B after A re-routes subsequent calls to B. A fix that
        /// merely cached the first selection (or that pinned the session to whatever it saw first)
        /// passes the test above and fails this one.
        /// </summary>
        [Fact]
        public async Task ReSelectingAnotherInstance_ReRoutesSubsequentToolCalls()
        {
            await using var host = await StartOAuthHostAsync();
            host.RegisterInstance(InstanceA, "unity", ProjectA, HashA, "MACHINE-A");
            await Task.Delay(50);
            host.RegisterInstance(InstanceB, "unity", ProjectB, HashB, "MACHINE-B");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var sessionId = await host.InitializeAsync(client);

            // Round 1 — select A (the non-MRU instance) and confirm routing follows it.
            var selectA = await host.CallToolAsync(client, sessionId, ServerNativeTools.SelectInstance,
                new { instance_id = InstanceA });
            selectA.IsError.ShouldBeFalse($"select_engine_instance(A) must succeed; got: {selectA.Text}");

            var beforeA = host.LastActiveAt(InstanceA);
            await host.CallToolAsync(client, sessionId, "any_engine_tool", new { });
            host.LastActiveAt(InstanceA).ShouldBeGreaterThan(beforeA,
                $"round 1: the call after selecting {InstanceA} must route to {InstanceA}");

            // Round 2 — switch the selection to B on the SAME session.
            var selectB = await host.CallToolAsync(client, sessionId, ServerNativeTools.SelectInstance,
                new { instance_id = InstanceB });
            selectB.IsError.ShouldBeFalse($"select_engine_instance(B) must succeed; got: {selectB.Text}");

            var beforeB2 = host.LastActiveAt(InstanceB);
            var beforeA2 = host.LastActiveAt(InstanceA);
            await host.CallToolAsync(client, sessionId, "any_engine_tool", new { });

            host.LastActiveAt(InstanceB).ShouldBeGreaterThan(beforeB2,
                $"round 2: after re-selecting {InstanceB} the call must route to {InstanceB}");
            host.LastActiveAt(InstanceA).ShouldBe(beforeA2,
                $"round 2: {InstanceA} is no longer selected and must not be routed to");
        }

        // ───────────────────────────── real loopback oauth host ─────────────────────────────

        /// <summary>
        /// Builds the REAL oauth server wiring on a loopback port. Hermetic: the JWKS provider is
        /// replaced with an in-memory key, so no authorization server is contacted. Modelled on
        /// <see cref="AmbientSessionIdOverHttpTests"/>, whose harness the target profile's
        /// <c>test.md</c> § Suite 4 nominates as the cheapest real-session harness to copy.
        /// </summary>
        static async Task<OAuthHost> StartOAuthHostAsync()
        {
            var port = FreeLoopbackPort();
            const string issuer = "https://as.example";              // never fetched: the JWKS provider is replaced below
            var publicUrl = $"http://127.0.0.1:{port}/mcp";

            var key = TestJwt.CreateKey();

            var dataArguments = new DataArguments(new[]
            {
                $"port={port}",
                "client-transport=streamableHttp",
                "auth=oauth",
                $"auth-issuer={issuer}",
                $"public-url={publicUrl}",
                // The proxied probe call cannot be answered (no live plugin behind the registered
                // connection ids), so keep ClientUtils' per-attempt wait short. Routing — the thing
                // under test — has already happened on the first attempt.
                "plugin-timeout=200",
            });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            // Hermetic: serve the signing key from memory instead of fetching the AS over HTTP.
            // Registered AFTER the production wiring so this singleton is the resolved one.
            builder.Services.AddSingleton<IJwksKeyProvider>(new FakeJwksKeyProvider().Add(Kid, key));

            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();

            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(
                issuer, publicUrl, DateTimeOffset.UtcNow.AddHours(1),
                sub: Account, scope: ConnectionIdentity.ScopeAgent));

            return new OAuthHost(app, $"http://127.0.0.1:{port}", token, key);
        }

        sealed class OAuthHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            readonly ECDsa _key;
            readonly string _baseUrl;
            readonly string _token;

            public OAuthHost(WebApplication app, string baseUrl, string token, ECDsa key)
            {
                _app = app;
                _baseUrl = baseUrl;
                _token = token;
                _key = key;
            }

            public IServiceProvider Services => _app.Services;

            AccountMcpStrategy Strategy
            {
                get
                {
                    var strategy = _app.Services.GetRequiredService<IMcpConnectionStrategy>() as AccountMcpStrategy;
                    strategy.ShouldNotBeNull("oauth mode must resolve the AccountMcpStrategy");
                    return strategy!;
                }
            }

            /// <summary>
            /// Registers a plugin instance exactly as <c>McpServerHub</c> does in oauth mode. Distinct
            /// project hash + machine per instance so the registry's dedup key (path, engine, machine)
            /// keeps both alive — identical metadata would silently evict the first.
            /// </summary>
            public void RegisterInstance(string instanceId, string engine, string projectName, string projectPathHash, string machineName)
            {
                var identity = ConnectionIdentity.Create(Account, ConnectionIdentity.ScopePlugin);
                identity.ShouldNotBeNull();
                Strategy.RegisterInstance(
                    identity!,
                    new PluginInstanceMetadata(instanceId, engine, projectName, projectPathHash, machineName),
                    connectionId: "conn-" + instanceId,
                    logger: NullLogger.Instance);
            }

            /// <summary>The instance an unpinned, unselected session of this account resolves to (the MRU rung).</summary>
            public string? ResolveWithoutSelection()
                => Strategy.Instances.Resolve(Account, projectPin: null, selectedInstanceId: null).Instance?.InstanceId;

            /// <summary>The registry's live activity stamp for an instance — bumped by routing, and by nothing else here.</summary>
            public DateTimeOffset LastActiveAt(string instanceId)
                => Strategy.Instances.GetInstances(Account)
                    .First(i => string.Equals(i.InstanceId, instanceId, StringComparison.Ordinal))
                    .LastActiveAt;

            /// <summary>Runs the MCP handshake and returns the server-issued <c>Mcp-Session-Id</c>.</summary>
            public async Task<string> InitializeAsync(HttpClient client)
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
                        clientInfo = new { name = "selection-routing-test", version = "1.0.0" }
                    }
                });
                sessionId.ShouldNotBeNullOrEmpty("the server must mint an Mcp-Session-Id on initialize");
                await PostAsync(client, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" });
                return sessionId!;
            }

            public async Task<(bool IsError, string Text)> CallToolAsync(HttpClient client, string sessionId, string toolName, object arguments)
            {
                var (body, _) = await PostAsync(client, sessionId, new
                {
                    jsonrpc = "2.0",
                    id = 99,
                    method = "tools/call",
                    @params = new { name = toolName, arguments }
                });
                return ReadToolResult(body);
            }

            public async Task<(string Body, string? SessionId)> PostAsync(HttpClient client, string? sessionId, object payload)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (!string.IsNullOrEmpty(sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                // Assert SUCCESS, not merely "not 401": a routing or wiring regression answering 404 /
                // 405 / 500 would otherwise resurface as a JsonDocument.Parse failure in ReadToolResult,
                // which reads as a broken harness rather than as the regression it is.
                // (notifications/initialized answers 202, so this cannot be pinned to 200.)
                response.IsSuccessStatusCode.ShouldBeTrue(
                    $"POST /mcp must succeed with the agent token; status {(int)response.StatusCode} {response.StatusCode}, body: {body}");
                var issued = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
                return (body, issued);
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _key.Dispose();
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

        static (bool IsError, string Text) ReadToolResult(string rawBody)
        {
            var body = Unwrap(rawBody);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                return (true, "JSON-RPC error: " + error.ToString());

            var result = root.GetProperty("result");
            var isError = result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
            var texts = new List<string>();
            if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                        texts.Add(text.GetString() ?? string.Empty);
                }
            }
            return (isError, string.Join("\n", texts));
        }
    }
}
