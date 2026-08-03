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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Common.Utils;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
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
    /// <para>The two catalog cases are one fixture family on purpose. The failure case asserts an
    /// EMPTY catalog, and an emptiness assertion is satisfied for free by any path that never ran at
    /// all — so it is paired with <see cref="PromptsList_WhenThePluginReturnsPrompts_EmitsThemOverTheWire"/>,
    /// which drives the SAME host and the SAME seam and pins the exact prompt names that come back.
    /// The failure case additionally pins <see cref="FakePromptHub.ListCallCount"/>, so the empty
    /// catalog is provably the DEGRADATION of a call that reached the plugin seam and failed there,
    /// not the silence of a request that never arrived.</para>
    ///
    /// <para>The operator-log half uses the shared <see cref="CapturedRouterLogs"/> helper; see
    /// <see cref="RouterLogCaptureTests"/> for the tests that pin the helper's own guarantees.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PromptListDegradationOverHttpTests
    {
        /// <summary>
        /// The shared identity of this fixture family: the text the plugin call fails with. It is what
        /// the client used to see in the synthetic prompt's description, and what the operator log must
        /// still carry.
        /// </summary>
        const string PluginFailureText =
            "Invoke 'RunListPrompts': Failed to invoke " +
            "'com.IvanMurzak.McpPlugin.Common.Model.RequestListPrompts' after 10 retries.";

        /// <summary>The client name this fixture family handshakes with.</summary>
        const string ClientName = "prompt-list-degradation-test";

        /// <summary>
        /// The regression. With the plugin call failing, <c>prompts/list</c> answers with an empty
        /// catalog and NO synthetic entry — in particular nothing named <c>"Error"</c>.
        /// </summary>
        [Fact]
        public async Task PromptsList_WhenThePluginCallFails_ReturnsEmptyCatalogWithNoSyntheticErrorPrompt()
        {
            var hub = FakePromptHub.ThatFailsWith(PluginFailureText);

            await using var host = await NoneAuthMcpHost.StartAsync(services => services.AddSingleton<IClientPromptHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, ClientName);
            var body = await host.CallAsync(client, sessionId, "prompts/list");

            using var doc = JsonDocument.Parse(body);

            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                $"prompts/list must not fail the JSON-RPC call when the plugin call fails; got: {body}");

            doc.RootElement.GetProperty("result").TryGetProperty("prompts", out var prompts).ShouldBeTrue(
                $"the degraded catalog is empty, not absent; got: {body}");

            var names = prompts.EnumerateArray()
                .Select(p => p.TryGetProperty("name", out var n) ? n.GetString() : null)
                .ToList();

            // Emptiness is asserted rather than "contains nothing named Error": an entry named anything
            // else would be just as much a fabrication, and emptiness cannot be satisfied by a
            // differently-named synthetic element. The names are projected rather than counted so a
            // failure names the fabricated entry instead of only its count.
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

            await using var host = await NoneAuthMcpHost.StartAsync(services => services.AddSingleton<IClientPromptHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, ClientName);
            var body = await host.CallAsync(client, sessionId, "prompts/list");

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

        /// <summary>
        /// The OTHER half of the ruling that produced this change: the client-facing fabrication goes
        /// away, but the internal failure text must still reach the OPERATOR. Removing the synthetic
        /// prompt deleted the only client-visible copy of that text, so the router's Warn log is now
        /// its ONLY carrier — and an unasserted log is one refactor away from silence. This pins it as
        /// a PRESENCE claim (the text must appear), which no short-circuiting path can satisfy.
        /// </summary>
        [Fact]
        public async Task PromptsList_WhenThePluginCallFails_StillLogsTheInternalFailureTextForOperators()
        {
            var hub = FakePromptHub.ThatFailsWith(PluginFailureText);

            using var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));

            await using var host = await NoneAuthMcpHost.StartAsync(services => services.AddSingleton<IClientPromptHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, ClientName);
            await host.CallAsync(client, sessionId, "prompts/list");

            logs.Text.ShouldContain(PluginFailureText, Case.Sensitive,
                "the failure text the client no longer sees must still reach the operator log at Warn");

            // Positive artifact for the capture itself: the router really did reach the failure
            // branch. Without it, a capture that silently recorded nothing would be indistinguishable
            // from one whose assertion above simply had not been reached yet.
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
            readonly ResponseData<ResponseListPrompts> _response;
            int _listCallCount;

            FakePromptHub(ResponseData<ResponseListPrompts> response) => _response = response;

            public int ListCallCount => Volatile.Read(ref _listCallCount);

            public static FakePromptHub ThatFailsWith(string message)
                => new FakePromptHub(ResponseData<ResponseListPrompts>.Error("req-fail", message));

            // Packed exactly as the real producer packs it (McpPromptManager.ListAll -> Pack), so the
            // success envelope this fixture feeds the router carries the same Message the plugin would.
            public static FakePromptHub ThatReturns(params ResponsePrompt[] prompts)
                => new FakePromptHub(new ResponseListPrompts { Prompts = prompts.ToList() }.Pack("req-ok"));

            public Task<ResponseData<ResponseListPrompts>> RunListPrompts(RequestListPrompts request, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _listCallCount);
                return _response.TaskFromResult();
            }

            public Task<ResponseData<ResponseGetPrompt>> RunGetPrompt(RequestGetPrompt request)
                => throw new NotSupportedException("These tests only drive prompts/list.");
        }
    }
}
