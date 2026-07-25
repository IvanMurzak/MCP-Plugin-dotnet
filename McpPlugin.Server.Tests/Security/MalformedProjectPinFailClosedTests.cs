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
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.Security
{
    /// <summary>
    /// 🔒 A project pin that is PRESENT but MALFORMED must fail CLOSED.
    ///
    /// <para><b>The defect.</b> The pinned routes carry no route constraint on <c>{pin}</c>, so
    /// <c>/p/&lt;non-hex&gt;/api/tools/{name}</c> still matched an endpoint.
    /// <c>McpSessionTokenMiddleware</c> then validated the segment loosely and, on failure, set the ambient
    /// pin to <c>null</c> — indistinguishable from a genuinely unpinned request. <c>AccountInstances.Resolve</c>
    /// therefore skipped its strict pinned branch entirely and fell through to
    /// <c>sticky → single → MRU</c>, so a caller that believed it was pinned to project A silently EXECUTED
    /// its tool call against an arbitrary sibling project B.</para>
    ///
    /// <para>The failure was asymmetric, which is what made it dangerous:
    /// a wrong-but-HEX pin failed closed (<c>NoMatchPinned</c>, never MRU) — only a MALFORMED one failed open.
    /// Pinning exists precisely to stop cross-project mis-routing, so a silent downgrade to "unpinned"
    /// defeated the control it was built for.</para>
    ///
    /// <para><b>ABSENT ≠ MALFORMED.</b> The genuinely-unpinned surfaces (<c>/api/tools</c>,
    /// <c>/api/system-tools</c>) are unpinned BY DESIGN and must keep working exactly as before — the
    /// no-collateral-damage tests below pin that.</para>
    ///
    /// Drives a REAL loopback host through the production <see cref="McpSessionTokenMiddleware"/> and all four
    /// REST groups; the tool hubs report the outcome of the real <see cref="AccountInstances.Resolve"/> AND
    /// record every invocation, so "the sibling project never executed anything" is asserted directly rather
    /// than inferred from a status code.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class MalformedProjectPinFailClosedTests
    {
        // pin = leading hex prefix of the project-path SHA-256 (validated as 1–64 hex chars).
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        const string PinB = "11223344";
        const string WrongButHexPin = "deadbeef";
        const string MalformedPin = "not-a-pin";
        const string Account = "acc-malformed-pin";
        const string InstanceA = "instance-A";
        const string InstanceB = "instance-B";

        /// <summary>Every shape the route accepts but the pin grammar (1–64 hex) rejects.</summary>
        public static TheoryData<string> MalformedPins() => new TheoryData<string>
        {
            "not-a-pin",   // punctuation
            "zzzz",        // out-of-alphabet letters
            "1234g",       // one bad nibble in an otherwise-hex value
            "aabbccdd0011223344556677889900aabbccdd0011223344556677889900aabbc", // 65 hex chars (over the cap)
        };

        // ───────── The regression: a malformed pin must not execute against a sibling project ─────────

        [Theory]
        [MemberData(nameof(MalformedPins))]
        public async Task MalformedPin_ToolCall_Rejected_AndNeverReachesAnyProject(string malformed)
        {
            // TWO live instances, so an unpinned/downgraded resolution HAS a sibling to mis-route to.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"/p/{malformed}/api/tools/ping");

            resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync()).ShouldContain(McpSessionTokenMiddleware.InvalidProjectPinError);
            // The decisive assertion: no project executed the call. Before the fix this recorded a routed
            // instance id (whichever was most-recently-active) — cross-project execution.
            host.ToolHub.Invocations.ShouldBeEmpty("a malformed pin must never execute a tool anywhere");
        }

        [Theory]
        [MemberData(nameof(MalformedPins))]
        public async Task MalformedPin_SystemToolCall_Rejected_AndNeverReachesAnyProject(string malformed)
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await CallAsync(client, $"/p/{malformed}/api/system-tools/ping");

            resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await resp.Content.ReadAsStringAsync()).ShouldContain(McpSessionTokenMiddleware.InvalidProjectPinError);
            host.SystemToolHub.Invocations.ShouldBeEmpty("a malformed pin must never execute a system tool anywhere");
        }

        [Fact]
        public async Task MalformedPin_ToolList_Rejected_AndNeverReachesAnyProject()
        {
            // The list surface leaks the sibling project's tool inventory, so it must fail closed too.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await client.GetAsync($"/p/{MalformedPin}/api/tools");

            resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            host.ToolHub.Invocations.ShouldBeEmpty();
        }

        [Fact]
        public async Task MalformedPin_SystemToolList_Rejected_AndNeverReachesAnyProject()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await client.GetAsync($"/p/{MalformedPin}/api/system-tools");

            resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            host.SystemToolHub.Invocations.ShouldBeEmpty();
        }

        // ───────── Guarding the behaviour that was ALREADY correct (must not regress) ─────────

        [Fact]
        public async Task WrongButHexPin_StillFailsClosed_NoMatchPinned_NeverMru()
        {
            // A well-formed pin that matches no live instance is a DIFFERENT case: the request is valid,
            // strict resolution runs, and it yields NoMatchPinned — never a sibling. Unchanged by this fix.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, $"/p/{WrongButHexPin}/api/tools")).ShouldBe("NoMatchPinned");
            (await ListOutcomeAsync(client, $"/p/{WrongButHexPin}/api/system-tools")).ShouldBe("NoMatchPinned");
        }

        [Fact]
        public async Task MatchingPin_StillResolvesStrictlyToItsOwnProject()
        {
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            (await ListOutcomeAsync(client, $"/p/{PinA}/api/tools")).ShouldBe(InstanceA);
            (await ListOutcomeAsync(client, $"/p/{PinB}/api/tools")).ShouldBe(InstanceB);
            (await ListOutcomeAsync(client, $"/p/{PinA}/api/system-tools")).ShouldBe(InstanceA);
            (await ListOutcomeAsync(client, $"/p/{PinB}/api/system-tools")).ShouldBe(InstanceB);
        }

        // ───────── No collateral damage: absent ≠ malformed ─────────

        [Fact]
        public async Task GenuinelyUnpinnedRequest_StillSucceeds()
        {
            // /api/tools carries no /p/ marker at all. It is unpinned BY DESIGN and must keep resolving
            // (sticky → single → MRU) exactly as before — rejecting it would be the collateral damage.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            var live = new[] { InstanceA, InstanceB };
            live.ShouldContain(await ListOutcomeAsync(client, "/api/tools"));
            live.ShouldContain(await ListOutcomeAsync(client, "/api/system-tools"));

            using var call = await CallAsync(client, "/api/tools/ping");
            call.StatusCode.ShouldBe(HttpStatusCode.OK);
            host.ToolHub.Invocations.ShouldNotBeEmpty("an unpinned call must still reach a project");
        }

        [Fact]
        public async Task UnpinnedRoutesAreUntouched_WhenNoPinnedGroupIsInvolved()
        {
            // A path segment that merely LOOKS pin-adjacent (a tool literally named "p") is not a pin
            // marker — nothing follows it — so the request stays unpinned rather than being rejected.
            await using var host = await StartHostAsync(TwoInstanceRegistry());
            using var client = NewClient(host);

            using var resp = await CallAsync(client, "/api/tools/p");
            resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // ───────── Middleware-level: the rejection is terminal and leaks no ambient state ─────────

        [Theory]
        [MemberData(nameof(MalformedPins))]
        public async Task Middleware_MalformedPin_ShortCircuits_AndSetsNoAmbientPin(string malformed)
        {
            var nextCalled = false;
            string? pinSeenDownstream = "<unset>";
            var middleware = new McpSessionTokenMiddleware(_ =>
            {
                nextCalled = true;
                pinSeenDownstream = McpSessionTokenContext.CurrentProjectPin;
                return Task.CompletedTask;
            });

            var ctx = new DefaultHttpContext { RequestServices = EmptyProvider };
            ctx.Request.Path = new PathString($"/p/{malformed}/api/tools/ping");
            ctx.Response.Body = new System.IO.MemoryStream();

            await middleware.InvokeAsync(ctx);

            ctx.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
            nextCalled.ShouldBeFalse("the pipeline must not continue past a malformed pin");
            pinSeenDownstream.ShouldBe("<unset>");
            McpSessionTokenContext.CurrentProjectPin.ShouldBeNull();
        }

        [Fact]
        public async Task Middleware_AbsentPin_PassesThrough()
        {
            var nextCalled = false;
            var middleware = new McpSessionTokenMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

            var ctx = new DefaultHttpContext { RequestServices = EmptyProvider };
            ctx.Request.Path = new PathString("/api/tools/ping");
            ctx.Response.Body = new System.IO.MemoryStream();

            await middleware.InvokeAsync(ctx);

            nextCalled.ShouldBeTrue("an unpinned request must be untouched");
        }

        // ───────────────────────────────── helpers ─────────────────────────────────

        static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

        static AccountInstances TwoInstanceRegistry()
        {
            var instances = new AccountInstances();
            instances.Register(Account, new PluginInstanceMetadata(InstanceA, "unity", "GameA", HashA, "PC-1"), "conn-A");
            instances.Register(Account, new PluginInstanceMetadata(InstanceB, "godot", "GameB", HashB, "PC-2"), "conn-B");
            return instances;
        }

        static HttpClient NewClient(RunningHost host) => new HttpClient { BaseAddress = new Uri(host.BaseUrl) };

        static Task<HttpResponseMessage> CallAsync(HttpClient client, string route)
            => client.PostAsync(route, new StringContent("{}", Encoding.UTF8, "application/json"));

        /// <summary>GET a list route and read back the single reported resolution outcome.</summary>
        static async Task<string?> ListOutcomeAsync(HttpClient client, string route)
        {
            using var resp = await client.GetAsync(route);
            resp.StatusCode.ShouldBe(HttpStatusCode.OK, $"{route} must reach the list handler");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            doc.RootElement.GetArrayLength().ShouldBe(1);
            return doc.RootElement[0].GetProperty("name").GetString();
        }

        // ── Minimal in-memory host: the production pin-capture middleware + all four REST groups. ──

        static async Task<RunningHost> StartHostAsync(AccountInstances instances)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            // none mode: no auth gate — this suite isolates PIN handling. (Auth parity lives in the
            // Pinned*AuthGatingTests suites.) none is also the worst case: an unauthenticated caller.
            var dataArguments = Mock.Of<IDataArguments>(d => d.Authorization == Consts.MCP.Server.AuthOption.none);
            var toolHub = new RecordingToolHub(instances, Account);
            var systemToolHub = new RecordingSystemToolHub(instances, Account);

            builder.Services.AddSingleton(dataArguments);
            builder.Services.AddSingleton<IClientToolHub>(toolHub);
            builder.Services.AddSingleton<IClientSystemToolHub>(systemToolHub);

            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0"); // OS-assigned loopback port
            app.UseMiddleware<McpSessionTokenMiddleware>();
            app.MapDirectToolCallApi(dataArguments);
            app.MapPinnedDirectToolCallApi(dataArguments);
            app.MapSystemToolApi(dataArguments);
            app.MapPinnedSystemToolApi(dataArguments);

            await app.StartAsync();
            return new RunningHost(app, app.Urls.First(), toolHub, systemToolHub);
        }

        sealed class RunningHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            public string BaseUrl { get; }
            public RecordingToolHub ToolHub { get; }
            public RecordingSystemToolHub SystemToolHub { get; }

            public RunningHost(WebApplication app, string baseUrl, RecordingToolHub toolHub, RecordingSystemToolHub systemToolHub)
            {
                _app = app;
                BaseUrl = baseUrl;
                ToolHub = toolHub;
                SystemToolHub = systemToolHub;
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        /// <summary>
        /// Resolves the CURRENT request against the real <see cref="AccountInstances"/> using the pin the
        /// middleware captured, records WHICH project each invocation landed on, and reports the outcome as
        /// the tool result. The recording is what turns "fails closed" into a directly observable property:
        /// an empty record proves no sibling project was ever driven.
        /// </summary>
        abstract class RecordingHubBase
        {
            readonly AccountInstances _instances;
            readonly string _account;
            readonly ConcurrentQueue<string> _invocations = new ConcurrentQueue<string>();

            protected RecordingHubBase(AccountInstances instances, string account)
            {
                _instances = instances;
                _account = account;
            }

            /// <summary>The resolution outcome of every request that reached this hub, in order.</summary>
            public IReadOnlyCollection<string> Invocations => _invocations.ToArray();

            protected string RecordAndResolve(out InstanceResolution resolution)
            {
                resolution = _instances.Resolve(
                    _account,
                    McpSessionTokenContext.CurrentProjectPin,
                    McpSessionTokenContext.CurrentSelectedInstanceId);

                var outcome = resolution.Kind switch
                {
                    InstanceResolutionKind.Resolved => resolution.Instance!.InstanceId,
                    InstanceResolutionKind.NoMatchPinned => "NoMatchPinned",
                    _ => "AccountEmpty"
                };
                _invocations.Enqueue(outcome);
                return outcome;
            }

            protected ResponseData<ResponseListTool[]> ListResponse(RequestListTool request)
            {
                var outcome = RecordAndResolve(out _);
                return new ResponseData<ResponseListTool[]>(request.RequestID, ResponseStatus.Success)
                {
                    Value = new[]
                    {
                        new ResponseListTool
                        {
                            Name = outcome,
                            Enabled = true,
                            InputSchema = Consts.MCP.EmptyInputSchema
                        }
                    }
                };
            }

            protected ResponseData<ResponseCallTool> CallResponse(RequestCallTool request)
            {
                RecordAndResolve(out var resolution);
                return resolution.Kind switch
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
                };
            }
        }

        sealed class RecordingToolHub : RecordingHubBase, IClientToolHub
        {
            public RecordingToolHub(AccountInstances instances, string account) : base(instances, account) { }

            public Task<ResponseData<ResponseListTool[]>> RunListTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ListResponse(request));

            public Task<ResponseData<ResponseCallTool>> RunCallTool(RequestCallTool request)
                => Task.FromResult(CallResponse(request));
        }

        sealed class RecordingSystemToolHub : RecordingHubBase, IClientSystemToolHub
        {
            public RecordingSystemToolHub(AccountInstances instances, string account) : base(instances, account) { }

            public Task<ResponseData<ResponseListTool[]>> RunListSystemTool(RequestListTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(ListResponse(request));

            public Task<ResponseData<ResponseCallTool>> RunSystemTool(RequestCallTool request, CancellationToken cancellationToken = default)
                => Task.FromResult(CallResponse(request));
        }
    }
}
