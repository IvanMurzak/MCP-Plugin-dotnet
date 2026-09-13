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
using com.IvanMurzak.McpPlugin.Server.Tests.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure
{
    /// <summary>
    /// The <c>auth=oauth</c> sibling of <see cref="NoneAuthMcpHost"/>: a real loopback Kestrel host
    /// running the REAL production wiring over the REAL Streamable HTTP transport, with every
    /// request carrying a valid ES256 bearer token signed by an in-memory key.
    ///
    /// <para>It exists because <c>auth=none</c> cannot answer whether AUTHENTICATION changes the
    /// order in which ambient session state is established relative to the SDK's per-session
    /// <see cref="System.Threading.ExecutionContext"/> capture. In <c>oauth</c> mode
    /// <c>UseAuthentication</c> populates <c>HttpContext.User</c> and <c>UseAuthorization</c> runs
    /// the endpoint's <c>RequireAuthorization</c> gate — two extra stages around
    /// <see cref="McpSessionTokenMiddleware"/> — and a mode that only ever ran unauthenticated
    /// would answer for a pipeline production does not use.</para>
    ///
    /// <para>Hermetic: the JWKS provider is replaced with an in-memory fake, so the authorization
    /// server is never contacted.</para>
    /// </summary>
    public sealed class OAuthMcpHost : IAsyncDisposable
    {
        const string Kid = "oauth-mcp-host-kid";
        const string Issuer = "https://as.example";

        readonly WebApplication _app;
        readonly ECDsa _key;
        readonly string _baseUrl;
        readonly string _publicUrl;
        readonly string _token;

        OAuthMcpHost(WebApplication app, string baseUrl, string publicUrl, string token, ECDsa key)
        {
            _app = app;
            _baseUrl = baseUrl;
            _publicUrl = publicUrl;
            _token = token;
            _key = key;
        }

        public IServiceProvider Services => _app.Services;

        /// <summary>The account subject the DEFAULT bearer token carries.</summary>
        public string AccountId { get; private set; } = string.Empty;

        /// <summary>
        /// Mints a second valid token for a DIFFERENT account subject, signed by the same in-memory
        /// key. Used to ask whether ambient identity follows the credential presented on the
        /// in-flight request or the one that established the session — a question no single-token
        /// fixture can pose.
        /// </summary>
        public string TokenForAccount(string account)
            => TestJwt.SignEs256(_key, Kid, TestJwt.Claims(
                Issuer, _publicUrl, DateTimeOffset.UtcNow.AddHours(1),
                sub: account, scope: ConnectionIdentity.ScopeAgent));

        public static async Task<OAuthMcpHost> StartAsync(
            Action<IServiceCollection>? registerFakes = null,
            string account = "acct-oauth-mcp-host")
        {
            var port = FreeLoopbackPort();
            var publicUrl = $"http://127.0.0.1:{port}/mcp";

            var key = TestJwt.CreateKey();

            var dataArguments = new DataArguments(new[]
            {
                $"port={port}",
                "client-transport=streamableHttp",
                "auth=oauth",
                $"auth-issuer={Issuer}",
                $"public-url={publicUrl}",
            });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            // Registered AFTER the production wiring so these are the resolved singletons.
            builder.Services.AddSingleton<IJwksKeyProvider>(new FakeJwksKeyProvider().Add(Kid, key));
            registerFakes?.Invoke(builder.Services);

            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();

            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(
                Issuer, publicUrl, DateTimeOffset.UtcNow.AddHours(1),
                sub: account, scope: ConnectionIdentity.ScopeAgent));

            return new OAuthMcpHost(app, $"http://127.0.0.1:{port}", publicUrl, token, key) { AccountId = account };
        }

        /// <summary>initialize + notifications/initialized; returns the server-minted session id.</summary>
        public async Task<string> HandshakeAsync(
            HttpClient client,
            string clientName = "mcp-plugin-server-oauth-test",
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

        public async Task<string> CallAsync(
            HttpClient client,
            string sessionId,
            string method,
            int id = 2,
            IReadOnlyDictionary<string, string>? headers = null,
            string? bearerToken = null)
        {
            var (body, _) = await PostAsync(client, sessionId, new { jsonrpc = "2.0", id, method }, headers, bearerToken);
            return Unwrap(body);
        }

        /// <summary>
        /// Sends a request WITHOUT asserting success, and returns the status alongside the body.
        /// The only caller is the case that expects a rejection; every other caller must keep going
        /// through <see cref="PostAsync"/>, whose success assertion is what stops a 401/403 from
        /// silently satisfying an "and nothing was emitted" claim.
        /// </summary>
        public async Task<(int Status, string Body)> PostExpectingAnyStatusAsync(
            HttpClient client,
            string? sessionId,
            object payload,
            string? bearerToken = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken ?? _token);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<(string Body, string? SessionId)> PostAsync(
            HttpClient client,
            string? sessionId,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            string? bearerToken = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken ?? _token);
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
            // SUCCESS, not merely "not 401": a 401 here would make every downstream "no metadata was
            // emitted" assertion pass for the wrong reason — the request never reaching a handler.
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
