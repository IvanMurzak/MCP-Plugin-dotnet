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
    /// End-to-end regression cover for the ambient MCP-session-id seam (issue #193).
    ///
    /// <para>These tests deliberately drive a REAL loopback Kestrel host running the REAL
    /// <c>WithMcpServer</c> + <c>WithMcpPluginServer</c> wiring over the REAL Streamable HTTP
    /// transport, and reach <see cref="SelectionToolContext.FromCurrent"/> the only way production
    /// does: through an actual MCP <c>initialize</c> handshake followed by an actual
    /// <c>tools/call</c>. That is the whole point. Every other <c>select_engine_instance</c> test in
    /// this suite hand-builds a <see cref="SelectionToolContext"/>, and a hand-built context CANNOT
    /// observe this defect by construction — the suite was fully green while the tool failed 100% of
    /// the time in production.</para>
    ///
    /// <para>The defect: <c>HttpServerTransportOptions.PerSessionExecutionContext</c> is <c>true</c>,
    /// so a session's request handlers run on the <see cref="System.Threading.ExecutionContext"/>
    /// captured inside <c>RunSessionHandler</c> — which executes during <c>initialize</c>. Per the MCP
    /// spec <c>initialize</c> carries no <c>Mcp-Session-Id</c> header (the server MINTS the id in that
    /// response), so <c>McpSessionTokenMiddleware</c>'s per-request <c>AsyncLocal</c> write could
    /// never reach a handler. The session id therefore has to be published from inside
    /// <c>RunSessionHandler</c>, exactly like the bearer token beside it.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class AmbientSessionIdOverHttpTests
    {
        const string Kid = "ambient-session-kid";
        const string Account = "acct-ambient-session";
        const string InstanceId = "instance-ambient-1";
        const string ProjectName = "AmbientProject";

        /// <summary>
        /// The regression: over a real MCP session, <c>select_engine_instance</c> succeeds AND the
        /// sticky selection lands in <see cref="ISessionSelectionStore"/> under the EXACT session id
        /// the server issued in the <c>initialize</c> response.
        ///
        /// <para>Both halves matter. The success text alone would only prove the ambient session id
        /// was non-empty; the store lookup pins its VALUE, so an implementation that published some
        /// other non-empty string (a connection id, a GUID minted per request) still fails here.</para>
        /// </summary>
        [Fact]
        public async Task SelectEngineInstance_OverRealMcpSession_UsesTheServerIssuedSessionId()
        {
            await using var host = await StartOAuthHostAsync();
            host.RegisterInstance(InstanceId, "unity", ProjectName);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var (_, sessionId) = await host.PostAsync(client, null, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "ambient-session-test", version = "1.0.0" }
                }
            });

            sessionId.ShouldNotBeNullOrEmpty("the server must mint an Mcp-Session-Id on initialize");

            await host.PostAsync(client, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" });

            var (selectBody, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = ServerNativeTools.SelectInstance, arguments = new { instance_id = InstanceId } }
            });

            var (isError, text) = ReadToolResult(selectBody);

            isError.ShouldBeFalse($"select_engine_instance must succeed over a real MCP session; got: {text}");
            text.ShouldContain($"({InstanceId})", Case.Sensitive,
                "the success text names the instance that was selected");
            text.ShouldContain($"unity:{ProjectName}", Case.Sensitive);

            // The value-pinning half: the sticky selection is keyed by the MCP session id, so this
            // only resolves when the ambient SessionId the handler saw WAS the server-issued one.
            var store = host.Services.GetRequiredService<ISessionSelectionStore>();
            store.Get(sessionId).ShouldBe(InstanceId,
                "the selection must be recorded under the exact Mcp-Session-Id the server issued");
        }

        /// <summary>
        /// <c>resources/list</c> with no engine plugin connected degrades to an EMPTY catalog instead
        /// of throwing. The two resource <c>SetError</c> overloads used to <c>throw</c>, which surfaced
        /// to the client as a hard JSON-RPC <c>-32603</c> "An error occurred." — turning the routine
        /// "plugin has not attached yet" window into a protocol error.
        /// </summary>
        [Fact]
        public async Task ResourceAndTemplateLists_WithNoPluginConnected_DegradeToEmptyInsteadOfProtocolError()
        {
            await using var host = await StartOAuthHostAsync();

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            var (_, sessionId) = await host.PostAsync(client, null, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "ambient-session-test", version = "1.0.0" }
                }
            });
            sessionId.ShouldNotBeNullOrEmpty();
            await host.PostAsync(client, sessionId, new { jsonrpc = "2.0", method = "notifications/initialized" });

            var (resourcesBody, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "resources/list"
            });

            using (var doc = JsonDocument.Parse(Unwrap(resourcesBody)))
            {
                doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                    $"resources/list must not fail the JSON-RPC call when no plugin is connected; got: {Unwrap(resourcesBody)}");
                doc.RootElement.GetProperty("result").GetProperty("resources")
                    .EnumerateArray().Count().ShouldBe(0, "the degraded catalog is empty, not absent");
            }

            var (templatesBody, _) = await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "resources/templates/list"
            });

            using (var doc = JsonDocument.Parse(Unwrap(templatesBody)))
            {
                doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                    $"resources/templates/list must not fail the JSON-RPC call when no plugin is connected; got: {Unwrap(templatesBody)}");
                doc.RootElement.GetProperty("result").GetProperty("resourceTemplates")
                    .EnumerateArray().Count().ShouldBe(0, "the degraded catalog is empty, not absent");
            }
        }

        // ───────────────────────────── real loopback oauth host ─────────────────────────────

        static async Task<OAuthHost> StartOAuthHostAsync()
        {
            var port = FreeLoopbackPort();
            var issuer = $"http://127.0.0.1:{FreeLoopbackPort()}";   // never fetched: the JWKS provider is replaced below
            var publicUrl = $"http://127.0.0.1:{port}/mcp";

            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var dataArguments = new DataArguments(new[]
            {
                $"port={port}",
                "client-transport=streamableHttp",
                "auth=oauth",
                $"auth-issuer={issuer}",
                $"public-url={publicUrl}",
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

            var token = SignEs256(key, Kid, new Dictionary<string, object>
            {
                ["iss"] = issuer,
                ["aud"] = publicUrl,
                ["sub"] = Account,
                ["scope"] = ConnectionIdentity.ScopeAgent,
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
            });

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

            public void RegisterInstance(string instanceId, string engine, string projectName)
            {
                var strategy = _app.Services.GetRequiredService<IMcpConnectionStrategy>() as AccountMcpStrategy;
                strategy.ShouldNotBeNull("oauth mode must resolve the AccountMcpStrategy");
                var identity = ConnectionIdentity.Create(Account, ConnectionIdentity.ScopePlugin);
                identity.ShouldNotBeNull();
                strategy!.RegisterInstance(
                    identity!,
                    new PluginInstanceMetadata(instanceId, engine, projectName, string.Empty, "TEST-MACHINE"),
                    connectionId: "conn-" + instanceId,
                    logger: NullLogger.Instance);
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
                response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, $"the agent token must be accepted; body: {body}");
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

        static string B64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        static string B64Url(string text) => B64Url(Encoding.UTF8.GetBytes(text));

        static string SignEs256(ECDsa key, string kid, IDictionary<string, object> claims)
        {
            var header = new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT", ["kid"] = kid };
            var headerB64 = B64Url(JsonSerializer.Serialize(header));
            var payloadB64 = B64Url(JsonSerializer.Serialize(claims));
            var signingInput = Encoding.ASCII.GetBytes(headerB64 + "." + payloadB64);
            var signature = key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return headerB64 + "." + payloadB64 + "." + B64Url(signature);
        }
    }
}
