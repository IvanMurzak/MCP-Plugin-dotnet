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
using System.Net.Sockets;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Security;
using com.IvanMurzak.McpPlugin.Server.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.IsolationMatrix
{
    /// <summary>
    /// The transport-security legs of the mcp-authorize Phase-2 gate (task b8): the DNS-rebinding
    /// Origin-403 defense driven over a REAL loopback Kestrel host, and the stdio same-project
    /// port-contention contract exercised over REAL sockets. Companion to
    /// <see cref="Phase2GateIsolationMatrixTests"/> (the routing/notification/selection/pin planes);
    /// together they are the full b8 isolation + selection + strict-pin + Origin + stdio matrix.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class Phase2GateTransportMatrixTests
    {
        // ════════════════════════════════ Origin 403 — live loopback host (all modes) ════════════════════════════════

        /// <summary>
        /// A present-but-non-allowed <c>Origin</c> on a guarded MCP path is rejected with <c>403</c>
        /// before routing/auth — over a real Kestrel loopback host with the real
        /// <see cref="OriginValidationMiddleware"/> wired. Verified on both the MCP endpoint and the
        /// SignalR hub negotiate path, in the allow sets used by both <c>none</c> and <c>oauth</c> modes.
        /// </summary>
        [Theory]
        [InlineData("/mcp")]
        [InlineData("/hub/mcp-server/negotiate")]
        public async Task Origin_Live_NonAllowedOrigin_Returns403_OnGuardedPaths(string path)
        {
            // oauth-mode allow set: the RS's own public-url origin + loopback.
            await using var host = await StartOriginHostAsync(allowedOrigin: "https://app.example:443");
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var req = new HttpRequestMessage(HttpMethod.Post, path);
            req.Headers.Add("Origin", "https://evil.example");

            var response = await client.SendAsync(req);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{path} with a hostile Origin must be rejected (403)");
        }

        /// <summary>
        /// A native (no-Origin) client and a loopback-origin browser BOTH pass the live guard on a
        /// guarded path — the guard blocks cross-site rebinding, not legitimate local/native traffic.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("http://localhost:6123")]
        [InlineData("http://127.0.0.1:9999")]
        public async Task Origin_Live_AbsentOrLoopbackOrigin_Passes(string? origin)
        {
            await using var host = await StartOriginHostAsync(allowedOrigin: "https://app.example:443");
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            if (origin != null)
                req.Headers.Add("Origin", origin);

            var response = await client.SendAsync(req);

            response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, "absent/loopback Origin must not be blocked");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        /// <summary>
        /// The RS's own <c>--public-url</c> origin is allowed (same-origin browser hosted at the RS);
        /// a discovery path is NOT guarded even with a hostile Origin.
        /// </summary>
        [Fact]
        public async Task Origin_Live_PublicUrlAllowed_AndDiscoveryPathUnguarded()
        {
            await using var host = await StartOriginHostAsync(allowedOrigin: "https://app.example:443");
            using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

            var sameOrigin = new HttpRequestMessage(HttpMethod.Post, "/mcp");
            sameOrigin.Headers.Add("Origin", "https://app.example");
            (await client.SendAsync(sameOrigin)).StatusCode.ShouldBe(HttpStatusCode.OK);

            var discovery = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource");
            discovery.Headers.Add("Origin", "https://evil.example");
            (await client.SendAsync(discovery)).StatusCode.ShouldBe(HttpStatusCode.OK, "discovery paths are intentionally not Origin-guarded");
        }

        // ════════════════════════════════ stdio same-project port contention (real sockets) ════════════════════════════════

        /// <summary>
        /// The stdio Flow-D contract: the first spawn owns the project's derived port; a later spawn
        /// for the SAME project detects the live server on that EXACT port and stands down with an
        /// actionable message (never self-starts a second server, never probes a neighbour). A second,
        /// DIFFERENT project's port is independent — per-project ports isolate concurrent projects.
        /// </summary>
        [Fact]
        public void Stdio_SameProjectPortOwned_SecondSpawnStandsDown_DifferentProjectIndependent()
        {
            // "Project 1" first spawn owns its derived port.
            var project1Owner = new TcpListener(IPAddress.Loopback, 0);
            project1Owner.Start();
            var project1Port = ((IPEndPoint)project1Owner.LocalEndpoint).Port;
            try
            {
                // A second stdio spawn for project 1 → Owned + actionable message, on that exact port.
                var contention = StdioServerGuard.CheckExactPort(project1Port);
                contention.IsOwned.ShouldBeTrue();
                contention.Status.ShouldBe(StdioPortStatus.Owned);
                contention.Port.ShouldBe(project1Port);
                contention.Message.ShouldNotBeNull();
                contention.Message!.ShouldContain(project1Port.ToString());
                contention.Message.ShouldContain("http"); // guidance: prefer the http config for multi-session

                // "Project 2" has a different derived port (its own editor) → free, this spawn may own it.
                var project2Port = FreeLoopbackPort();
                project2Port.ShouldNotBe(project1Port);
                var project2 = StdioServerGuard.CheckExactPort(project2Port);
                project2.Status.ShouldBe(StdioPortStatus.Available);
                project2.IsOwned.ShouldBeFalse();
            }
            finally
            {
                project1Owner.Stop();
            }
        }

        // ── Minimal real loopback host wiring only the Origin middleware + terminal endpoints. ──

        static async Task<RunningHost> StartOriginHostAsync(string allowedOrigin)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            var options = new OriginValidationOptions(new[] { allowedOrigin }, allowLoopback: true, hubPath: Consts.Hub.RemoteApp);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            app.UseMiddleware<OriginValidationMiddleware>(options);

            // Terminal endpoints — reached only when the Origin guard lets the request through.
            app.MapPost("/mcp", () => Results.Ok());
            app.MapPost("/hub/mcp-server/negotiate", () => Results.Ok());
            app.MapGet("/.well-known/oauth-protected-resource", () => Results.Ok());

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First());
        }

        static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
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
