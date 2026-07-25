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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Api;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// 📌 Pinned REST SYSTEM-tool routes (owner ruling 2026-07-25 — REVERSES design 06 D15). The pinned group
    /// <c>GET /p/{pin}/api/system-tools</c> + <c>POST /p/{pin}/api/system-tools/{name}</c> is the system-tool
    /// twin of the pinned <c>/api/tools</c> group: both prefixes register through ONE shared registrar
    /// (<c>SystemToolEndpoints.MapSystemToolGroup</c>), so they share handlers and the auth gate, and the pin
    /// is captured live from the request path by <see cref="McpSessionTokenMiddleware"/> and resolved STRICTLY
    /// (a wrong pin yields <c>NoMatchPinned</c> even with sibling instances present — never MRU; an empty
    /// account yields <c>AccountEmpty</c>).
    ///
    /// <para><b>Why this exists.</b> D15 declined a pinned system-tools route on the premise that "no engine
    /// registers a system <c>ping</c> tool" — false: Unity declares <c>ping</c> as
    /// <c>ToolType = McpToolType.System</c>. The cloud golden path is pinned (<c>/mcp/p/&lt;pin&gt;/…</c>), so
    /// without this group an engine-liveness probe over the system-tools surface was impossible there. Every
    /// route assertion below 404s without the pinned mapping — these are the tests that fail without the change.</para>
    ///
    /// Mirrors <see cref="PinnedDirectToolRoutingTests"/>, driving a REAL loopback host through the production
    /// middleware; the system-tool hub reports the outcome of the real <see cref="AccountInstances.Resolve"/>.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PinnedSystemToolRoutingTests
    {
        // pin = leading hex prefix of the project-path SHA-256 (McpSessionTokenMiddleware validates 1–64 hex).
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        const string PinB = "11223344";
        const string WrongPin = "deadbeef";
        const string Account = "acc-pinned-system";
        const string InstanceA = "instance-A";
        const string InstanceB = "instance-B";

        static string PinnedSystemTools(string pin) => $"/p/{pin}/api/system-tools";
        const string UnpinnedSystemTools = "/api/system-tools";

        // ─────────── Route existence — the D15 reversal (these 404 without the pinned mapping) ───────────

        [Fact]
        public async Task PinnedSystemToolsRoutes_AreMapped()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            // Control: the unpinned group is (and stays) mapped.
            (await client.GetAsync(UnpinnedSystemTools)).StatusCode.ShouldNotBe(HttpStatusCode.NotFound);

            // The pinned analog now exists — list AND call reach a handler instead of the route table's 404.
            (await client.GetAsync(PinnedSystemTools(PinA))).StatusCode.ShouldBe(HttpStatusCode.OK);
            using var post = await CallAsync(client, $"{PinnedSystemTools(PinA)}/ping");
            post.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // ─────────────── GET list path — strict pin → the pinned project's instance ───────────────

        [Fact]
        public async Task PinnedList_ResolvesStrictlyToTheMatchingInstance()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            // Each pin resolves ONLY to its own project's instance — never the sibling.
            (await ListOutcomeAsync(client, PinnedSystemTools(PinA))).ShouldBe(InstanceA);
            (await ListOutcomeAsync(client, PinnedSystemTools(PinB))).ShouldBe(InstanceB);
        }

        [Fact]
        public async Task PinnedList_WrongPinWithSiblingsPresent_NoMatchPinned_NeverMru()
        {
            // The account HAS two live instances, but neither matches the pin. A pin NEVER falls through to a
            // sibling (never MRU): the strict outcome is NoMatchPinned, not a routed instance id.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, PinnedSystemTools(WrongPin))).ShouldBe("NoMatchPinned");
        }

        [Fact]
        public async Task PinnedList_EmptyAccount_AccountEmpty()
        {
            await using var host = await StartHostAsync(new AccountInstances()); // no live instances at all
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, PinnedSystemTools(PinA))).ShouldBe("AccountEmpty");
        }

        [Fact]
        public async Task UnpinnedList_MultipleInstances_FallsToMru_ProvesPinnedStrictnessContrast()
        {
            // Contrast: the UNPINNED system-tools group carries no pin, so it resolves to MRU (one of the live
            // instances) — never NoMatchPinned/AccountEmpty. That ambiguity is exactly what the pinned group
            // removes, and it is why a cloud (pinned-path) client needs the pinned group to probe a KNOWN project.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            var outcome = await ListOutcomeAsync(client, UnpinnedSystemTools);
            new[] { InstanceA, InstanceB }.ShouldContain(outcome);
        }

        // ─────────────── POST call path — outcome → deterministic HTTP status ───────────────
        //
        // FIDELITY NOTE: the 404-vs-503 split below is produced by ResolutionReportingSystemToolHub's OWN
        // chosen ResponseErrorKinds (see the hub, bottom of this file). It pins the shared handler's
        // DirectHttpErrorMapper wiring — NOT the production status. In production AccountMcpStrategy
        // .ResolveConnectionId returns null for BOTH NoMatchPinned and AccountEmpty (the kind is discarded),
        // so ClientUtils.InvokeAsync exhausts its retries and yields Unavailable → 503 for either case.
        // Failing FAST on a known-unresolvable pin is a cross-cutting ClientUtils/strategy change affecting
        // all four REST groups, so it is deliberately out of scope for this PR.

        [Fact]
        public async Task PinnedCall_MatchingPin_Resolves_200()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            // The motivating case: a liveness probe of a KNOWN project over the pinned cloud path.
            using var resp = await CallAsync(client, $"{PinnedSystemTools(PinA)}/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await resp.Content.ReadAsStringAsync()).ShouldContain(InstanceA);
        }

        [Fact]
        public async Task PinnedCall_WrongPinWithSiblingsPresent_NoMatchPinned_404_NeverMru()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"{PinnedSystemTools(WrongPin)}/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await resp.Content.ReadAsStringAsync()).ShouldContain("NoMatchPinned");
        }

        [Fact]
        public async Task PinnedCall_EmptyAccount_AccountEmpty_503()
        {
            await using var host = await StartHostAsync(new AccountInstances());
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"{PinnedSystemTools(PinA)}/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await resp.Content.ReadAsStringAsync()).ShouldContain("AccountEmpty");
        }

        // ─────────────── Handler parity: pinned and unpinned run the SAME handlers ───────────────

        [Fact]
        public async Task MalformedJsonBody_PinnedAndUnpinned_Both400()
        {
            // Body handling lives in the shared CallSystemToolHandler; identical outcomes on both prefixes is
            // the observable proof the pinned group did not fork the logic.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var pinned = await PostRawAsync(client, $"{PinnedSystemTools(PinA)}/ping", "{ not json");
            using var unpinned = await PostRawAsync(client, $"{UnpinnedSystemTools}/ping", "{ not json");

            pinned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            unpinned.StatusCode.ShouldBe(pinned.StatusCode);
        }

        [Fact]
        public async Task NonObjectJsonBody_PinnedAndUnpinned_Both400()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var pinned = await PostRawAsync(client, $"{PinnedSystemTools(PinA)}/ping", "[1,2,3]");
            using var unpinned = await PostRawAsync(client, $"{UnpinnedSystemTools}/ping", "[1,2,3]");

            pinned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            unpinned.StatusCode.ShouldBe(pinned.StatusCode);
        }

        [Fact]
        public async Task ListPayloadShape_PinnedAndUnpinned_AreIdenticalForOneInstance()
        {
            // A single-instance account resolves identically with or without a pin, so the two groups must
            // return byte-identical list payloads — they are the same handler over the same hub.
            var instances = new AccountInstances();
            instances.Register(Account, new PluginInstanceMetadata(InstanceA, "unity", "GameA", HashA, "PC-1"), "conn-A");

            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            var pinned = await client.GetStringAsync(PinnedSystemTools(PinA));
            var unpinned = await client.GetStringAsync(UnpinnedSystemTools);

            pinned.ShouldBe(unpinned);
        }

        // ───────────────────────────────── helpers ─────────────────────────────────

        static AccountInstances TwoInstanceRegistry()
        {
            var instances = new AccountInstances();
            instances.Register(Account, new PluginInstanceMetadata(InstanceA, "unity", "GameA", HashA, "PC-1"), "conn-A");
            instances.Register(Account, new PluginInstanceMetadata(InstanceB, "godot", "GameB", HashB, "PC-2"), "conn-B");
            return instances;
        }

        static HttpClient NewClient(RunningHost host) => new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

        /// <summary>GET the (pinned or unpinned) list route and read back the single reported outcome name.</summary>
        static async Task<string?> ListOutcomeAsync(HttpClient client, string route)
        {
            using var resp = await client.GetAsync(route);
            resp.StatusCode.ShouldBe(HttpStatusCode.OK, $"{route} must reach the list handler (mapped route)");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetArrayLength().ShouldBe(1);
            return doc.RootElement[0].GetProperty("name").GetString();
        }

        static Task<HttpResponseMessage> CallAsync(HttpClient client, string route)
            => client.PostAsync(route, new StringContent("{}", Encoding.UTF8, "application/json"));

        static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string route, string body)
            => client.PostAsync(route, new StringContent(body, Encoding.UTF8, "application/json"));

        // ── Minimal in-memory host: the pin-capture middleware + both system-tool groups. ──

        static async Task<RunningHost> StartHostAsync(AccountInstances instances)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            // none mode: no auth gate — this suite isolates the ROUTING/RESOLUTION behavior (auth parity is
            // covered by PinnedSystemToolAuthGatingTests).
            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == Consts.MCP.Server.AuthOption.none);
            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientSystemToolHub>(new ResolutionReportingSystemToolHub(instances, Account));

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            // Production pin-capture middleware: parses /p/<pin> out of the request path into the ambient
            // McpSessionTokenContext, exactly as UseMcpPluginServer wires it in prod.
            app.UseMiddleware<McpSessionTokenMiddleware>();
            app.MapSystemToolApi(dataArguments);       // unpinned group
            app.MapPinnedSystemToolApi(dataArguments); // pinned group (under test)

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First());
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

        /// <summary>
        /// A SYSTEM tool hub that, instead of invoking a live plugin over SignalR, resolves the CURRENT request
        /// against the real <see cref="AccountInstances"/> using the pin the middleware captured from the path,
        /// and reports the resolution outcome (resolved instance id, or <c>NoMatchPinned</c>/<c>AccountEmpty</c>)
        /// as the tool result. This exercises the exact production seam the pinned system-tools routes wire up:
        /// pinned URL → <see cref="McpSessionTokenMiddleware"/> → ambient
        /// <see cref="McpSessionTokenContext.CurrentProjectPin"/> → strict <see cref="AccountInstances.Resolve"/>.
        /// </summary>
        sealed class ResolutionReportingSystemToolHub : IClientSystemToolHub
        {
            readonly AccountInstances _instances;
            readonly string _account;

            public ResolutionReportingSystemToolHub(AccountInstances instances, string account)
            {
                _instances = instances;
                _account = account;
            }

            InstanceResolution Resolve() => _instances.Resolve(
                _account,
                McpSessionTokenContext.CurrentProjectPin,
                McpSessionTokenContext.CurrentSelectedInstanceId);

            static string OutcomeName(InstanceResolution r) => r.Kind switch
            {
                InstanceResolutionKind.Resolved => r.Instance!.InstanceId,
                InstanceResolutionKind.NoMatchPinned => "NoMatchPinned",
                _ => "AccountEmpty"
            };

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = new[]
                    {
                        new ResponseListTool
                        {
                            Name = OutcomeName(Resolve()),
                            Enabled = true,
                            InputSchema = Consts.MCP.EmptyInputSchema
                        }
                    }
                });

            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
            {
                var resolution = Resolve();
                return Task.FromResult(resolution.Kind switch
                {
                    InstanceResolutionKind.Resolved =>
                        ResponseData<ResponseCallTool>.Success(request.RequestID)
                            .SetData(ResponseCallTool.Success(resolution.Instance!.InstanceId)),
                    InstanceResolutionKind.NoMatchPinned =>
                        ResponseData<ResponseCallTool>.Error(request.RequestID, "NoMatchPinned", ResponseErrorKind.NotFound)
                            .SetData(ResponseCallTool.Error("NoMatchPinned", ResponseErrorKind.NotFound)),
                    _ =>
                        ResponseData<ResponseCallTool>.Error(request.RequestID, "AccountEmpty", ResponseErrorKind.Unavailable)
                            .SetData(ResponseCallTool.Error("AccountEmpty", ResponseErrorKind.Unavailable)),
                });
            }
        }
    }
}
