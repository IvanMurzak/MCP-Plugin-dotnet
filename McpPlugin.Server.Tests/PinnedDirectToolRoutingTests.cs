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
using com.IvanMurzak.McpPlugin.Server.Webhooks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// 📌 Pinned REST tool routes (design 06 / zero-config-engine-connect b1). The pinned group
    /// <c>GET /p/{pin}/api/tools</c> + <c>POST /p/{pin}/api/tools/{name}</c> resolves the (account, pin)
    /// bucket STRICTLY by pin: a pin never falls through — a wrong pin returns <c>NoMatchPinned</c> even
    /// when the account HAS sibling instances (never MRU-routed), and an empty account returns
    /// <c>AccountEmpty</c>. The UNPINNED group is byte-identical wiring but still falls to MRU (the
    /// contrast that proves the pinned group is strict). There is deliberately NO pinned system-tools
    /// route (design 06 D15). These drive a REAL loopback host through the production
    /// <see cref="McpSessionTokenMiddleware"/> so the pin is captured live from the pinned request path
    /// exactly as in prod; the tool hub reports the outcome of the real <see cref="AccountInstances.Resolve"/>.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PinnedDirectToolRoutingTests
    {
        // pin = leading hex prefix of the project-path SHA-256 (McpSessionTokenMiddleware validates 1–64 hex).
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        const string PinB = "11223344";
        const string WrongPin = "deadbeef";
        const string Account = "acc-pinned";
        const string InstanceA = "instance-A";
        const string InstanceB = "instance-B";

        // ─────────────────── GET list path — strict pin → the pinned project's instance ───────────────────

        [Fact]
        public async Task PinnedList_ResolvesStrictlyToTheMatchingInstance()
        {
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            // Each pin resolves ONLY to its own project's instance — never the sibling.
            (await ListOutcomeAsync(client, $"/p/{PinA}/api/tools")).ShouldBe(InstanceA);
            (await ListOutcomeAsync(client, $"/p/{PinB}/api/tools")).ShouldBe(InstanceB);
        }

        [Fact]
        public async Task PinnedList_WrongPinWithSiblingsPresent_NoMatchPinned_NeverMru()
        {
            // The account HAS two live instances, but neither matches the pin. A pin NEVER falls through to
            // a sibling (never MRU): the strict outcome is NoMatchPinned, not a routed instance id.
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, $"/p/{WrongPin}/api/tools")).ShouldBe("NoMatchPinned");
        }

        [Fact]
        public async Task PinnedList_EmptyAccount_AccountEmpty()
        {
            var instances = new AccountInstances(); // account has NO live instances at all
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, $"/p/{PinA}/api/tools")).ShouldBe("AccountEmpty");
        }

        [Fact]
        public async Task UnpinnedList_MultipleInstances_FallsToMru_ProvesPinnedStrictnessContrast()
        {
            // Regression / contrast: the UNPINNED group carries no pin, so it resolves to MRU (one of the
            // live instances) — NEVER NoMatchPinned/AccountEmpty. This is exactly the ambiguity the pinned
            // group removes; the pinned tests above prove the pinned group does NOT do this.
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            var outcome = await ListOutcomeAsync(client, "/api/tools");
            new[] { InstanceA, InstanceB }.ShouldContain(outcome);
        }

        // ─────────────────── POST call path — outcome → deterministic HTTP status ───────────────────

        [Fact]
        public async Task PinnedCall_MatchingPin_Resolves_200()
        {
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"/p/{PinA}/api/tools/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task PinnedCall_WrongPinWithSiblingsPresent_NoMatchPinned_404_NeverMru()
        {
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"/p/{WrongPin}/api/tools/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await resp.Content.ReadAsStringAsync()).ShouldContain("NoMatchPinned");
        }

        [Fact]
        public async Task PinnedCall_EmptyAccount_AccountEmpty_503()
        {
            var instances = new AccountInstances();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"/p/{PinA}/api/tools/ping");
            resp.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await resp.Content.ReadAsStringAsync()).ShouldContain("AccountEmpty");
        }

        // ─────────────────── Route set (design 06 D15) — no pinned system-tools analog ───────────────────

        [Fact]
        public async Task NoPinnedSystemToolsRoute_Exists()
        {
            var instances = TwoInstanceRegistry();
            await using var host = await StartHostAsync(instances);
            using var client = NewClient(host);

            // Control: the UNPINNED system-tools route IS mapped (reachable, not 404, in none mode).
            (await client.GetAsync("/api/system-tools")).StatusCode.ShouldNotBe(HttpStatusCode.NotFound);

            // The pinned surface has NO system-tools analog — both list and call 404 (the route is not mapped).
            (await client.GetAsync($"/p/{PinA}/api/system-tools")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            using var post = await CallAsync(client, $"/p/{PinA}/api/system-tools/ping");
            post.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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

        // ── Minimal in-memory host: the pin-capture middleware + both tool-call groups + system-tools. ──

        static async Task<RunningHost> StartHostAsync(AccountInstances instances)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            // none mode: no auth gate — this suite isolates the ROUTING/RESOLUTION behavior (auth parity
            // is covered by PinnedDirectToolAuthGatingTests).
            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == Consts.MCP.Server.AuthOption.none);
            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientToolHub>(new ResolutionReportingToolHub(instances, Account));
            builder.Services.AddSingleton<IClientSystemToolHub>(new StubSystemToolHub());
            builder.Services.AddSingleton(Mock.Of<IAuthorizationWebhookService>());

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            // Production pin-capture middleware: parses /p/<pin> out of the request path into the ambient
            // McpSessionTokenContext, exactly as UseMcpPluginServer wires it in prod.
            app.UseMiddleware<McpSessionTokenMiddleware>();
            app.MapDirectToolCallApi(dataArguments);       // unpinned group
            app.MapPinnedDirectToolCallApi(dataArguments); // pinned group (under test)
            app.MapSystemToolApi(dataArguments);           // to prove there is NO pinned system-tools analog

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
        /// A tool hub that, instead of invoking a live plugin over SignalR, resolves the CURRENT request
        /// against the real <see cref="AccountInstances"/> using the pin the middleware captured from the
        /// path, and reports the resolution outcome (resolved instance id, or <c>NoMatchPinned</c>/
        /// <c>AccountEmpty</c>) as the tool result. This exercises the exact production seam the pinned
        /// routes wire up: pinned URL → <see cref="McpSessionTokenMiddleware"/> → ambient
        /// <see cref="McpSessionTokenContext.CurrentProjectPin"/> → strict <see cref="AccountInstances.Resolve"/>.
        /// </summary>
        sealed class ResolutionReportingToolHub : IClientToolHub
        {
            readonly AccountInstances _instances;
            readonly string _account;

            public ResolutionReportingToolHub(AccountInstances instances, string account)
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

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
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

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
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

        sealed class StubSystemToolHub : IClientSystemToolHub
        {
            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ResponseData<ResponseCallTool>.Success(request.RequestID));

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = Array.Empty<ResponseListTool>()
                });
        }
    }
}
