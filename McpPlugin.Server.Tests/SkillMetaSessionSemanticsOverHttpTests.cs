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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Pins the SESSION SEMANTICS of the <c>X-McpPlugin-Skill-Meta</c> opt-in over a REAL Streamable
    /// HTTP session: <c>initialize</c> → <c>notifications/initialized</c> → <c>tools/list</c>.
    ///
    /// <para><b>The ruling this file encodes.</b> The opt-in is a property of the SESSION, read from
    /// the <c>initialize</c> request and fixed for the session's whole life. A later request's
    /// header is ignored. See <c>StreamableHttpTransportLayer.RunSessionHandler</c> and
    /// <c>McpSessionTokenContext.IsSkillMetaClient</c> for the decision and the rejected
    /// alternative.</para>
    ///
    /// <para><b>Why this cannot be an in-process test.</b> The entire question lives in the
    /// transport's execution-context handling: <c>PerSessionExecutionContext</c> is <c>true</c>, so
    /// a <c>tools/list</c> handler runs on the <see cref="System.Threading.ExecutionContext"/>
    /// captured during <c>initialize</c>. A test that reaches
    /// <c>ExtensionsListMeta.BuildToolMeta</c> through a <c>DefaultHttpContext</c> — as every case
    /// in <c>SkillMetaClientGateTests</c> does — shares ONE execution flow with the middleware and
    /// is therefore green in every world, including the broken one. That gap is recorded in
    /// <c>docs/plant-logs/server-skill-meta-gate.md</c>; this file closes it.</para>
    ///
    /// <para><b>The discriminating property</b> of every case below is <i>which request carried the
    /// header</i>. Host, tool hub, tool fixture, HTTP client, JSON-RPC method and argument list are
    /// held identical across the four cells; only the header placement moves. Nothing else in the
    /// matrix varies, so a cell's outcome cannot be attributed to anything else.</para>
    ///
    /// <para><b>Both halves are exercised in every cell.</b> Each response is checked to carry BOTH
    /// tools by name before any <c>_meta</c> claim is made, so "no skill metadata" can never be
    /// satisfied by a missing tool, a dropped session or a JSON-RPC error; and the emitting cells
    /// assert the exact key set and the exact text, so "metadata emitted" cannot be satisfied by an
    /// empty object. The unattributed tool is the in-cell control for the opposite failure: it must
    /// carry no skill keys even in a cell where the opt-in is on, which is what distinguishes an
    /// attribute-driven emitter from one that decorates everything.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class SkillMetaSessionSemanticsOverHttpTests
    {
        static readonly IReadOnlyDictionary<string, string> SkillMetaOptIn = new Dictionary<string, string>
        {
            [Consts.MCP.Server.Headers.SkillMetaClient] = Consts.MCP.Server.Headers.SkillMetaClientOptInValue
        };

        static readonly IReadOnlyDictionary<string, string> TrustedOptIn = new Dictionary<string, string>
        {
            [Consts.MCP.Server.Headers.TrustedInternalClient] = Consts.MCP.Server.Headers.TrustedInternalClientOptInValue
        };

        // ─────────────────────────────────────────────────────────────────────
        // The 2×2 matrix, over the real transport
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Row 1 is the negative control (header nowhere). Row 2 is the case the whole task exists
        /// for: the header on <c>tools/list</c> ALONE does not opt in — deliberately, and now by
        /// construction rather than by middleware ordering. Rows 3 and 4 are the positive half: the
        /// header on <c>initialize</c> opts the session in whether or not it is repeated later.
        /// </summary>
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public async Task ToolsList_SkillMetadata_IsDecidedByTheInitializeRequestAlone(
            bool headerOnInitialize, bool headerOnToolsList, bool expectSkillKeys)
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "skill-meta-session-semantics",
                initializeHeaders: headerOnInitialize ? SkillMetaOptIn : null);

            var body = await host.CallAsync(client, sessionId, "tools/list", id: 2,
                headers: headerOnToolsList ? SkillMetaOptIn : null);

            AssertMatrixCell(body, expectSkillKeys, $"initialize={headerOnInitialize}, tools/list={headerOnToolsList}");

            // The request reached the plugin seam. Without this, every "absent" row would also hold
            // for a listing the router never produced.
            hub.ListSnapshots.Count.ShouldBe(1, "tools/list must have reached the plugin seam exactly once");
        }

        /// <summary>
        /// The ruling stated as the property a client author needs: within ONE session the answer
        /// never changes, in BOTH directions.
        ///
        /// <para>Two sessions, four listings. In the opted-OUT session the header is added on the
        /// second listing and changes nothing; in the opted-IN session it is dropped on the second
        /// listing and changes nothing. One direction alone would be satisfied by a server that
        /// simply always says no (or always says yes).</para>
        /// </summary>
        [Fact]
        public async Task WithinOneSession_TheAnswerNeverChanges_InEitherDirection()
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // Session A: opted OUT at initialize; adding the header later must not turn it on.
            var sessionOut = await host.HandshakeAsync(client, "skill-meta-sticky-out");
            AssertMatrixCell(await host.CallAsync(client, sessionOut, "tools/list", id: 2),
                expectSkillKeys: false, cell: "session-out, listing 1 (no header)");
            AssertMatrixCell(await host.CallAsync(client, sessionOut, "tools/list", id: 3, headers: SkillMetaOptIn),
                expectSkillKeys: false, cell: "session-out, listing 2 (header added mid-session)");

            // Session B: opted IN at initialize; dropping the header later must not turn it off.
            var sessionIn = await host.HandshakeAsync(client, "skill-meta-sticky-in", initializeHeaders: SkillMetaOptIn);
            AssertMatrixCell(await host.CallAsync(client, sessionIn, "tools/list", id: 4, headers: SkillMetaOptIn),
                expectSkillKeys: true, cell: "session-in, listing 1 (header repeated)");
            AssertMatrixCell(await host.CallAsync(client, sessionIn, "tools/list", id: 5),
                expectSkillKeys: true, cell: "session-in, listing 2 (header dropped mid-session)");

            hub.ListSnapshots.Count.ShouldBe(4, "all four listings must have reached the plugin seam");
        }

        /// <summary>
        /// Rules session RESUMPTION in: a session is the unit, so a NEW <c>initialize</c> re-decides
        /// the opt-in from scratch. A client that reconnects without repeating the header loses its
        /// skill metadata — the behaviour is not a leak, it is the ruling, and it is exactly the
        /// hazard a client author must know about.
        ///
        /// <para>The two sessions run on the SAME host and the SAME HTTP client, so the only thing
        /// that changed between them is the second handshake's headers.</para>
        /// </summary>
        [Fact]
        public async Task ANewSessionReDecidesTheOptIn_SoAReconnectWithoutTheHeaderLosesSkillMetadata()
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var firstSession = await host.HandshakeAsync(client, "skill-meta-reconnect", initializeHeaders: SkillMetaOptIn);
            AssertMatrixCell(await host.CallAsync(client, firstSession, "tools/list", id: 2),
                expectSkillKeys: true, cell: "session 1 (opted in)");

            var secondSession = await host.HandshakeAsync(client, "skill-meta-reconnect");
            secondSession.ShouldNotBe(firstSession, "the reconnect must be a genuinely new session");
            AssertMatrixCell(await host.CallAsync(client, secondSession, "tools/list", id: 3),
                expectSkillKeys: false, cell: "session 2 (reconnected without the header)");

            // ...and the FIRST session, still alive, still emits. This is what makes the second
            // session's silence a property of that session rather than of the host having entered
            // some global non-emitting state.
            AssertMatrixCell(await host.CallAsync(client, firstSession, "tools/list", id: 4),
                expectSkillKeys: true, cell: "session 1 again, after session 2 opted out");
        }

        /// <summary>
        /// The exact-ordinal opt-in rule must hold at the SESSION-establishing site too, not only in
        /// the middleware.
        ///
        /// <para>This is the drift assertion. <c>SkillMetaClientGateTests</c> pins the rule where the
        /// middleware reads the header; the value that decides a session is read at a second site,
        /// and a hand-copied comparison there — a truthiness test, a case-insensitive compare —
        /// would be invisible to every in-process case in this repository. Here the session is
        /// established by a real <c>initialize</c> carrying the wrong value, and the observable is
        /// what a client receives.</para>
        /// </summary>
        [Theory]
        [InlineData("0")]
        [InlineData("true")]
        [InlineData("yes")]
        [InlineData("TRUE")]
        [InlineData("01")]
        public async Task OptInHeaderOnInitialize_WithAnyOtherValue_DoesNotOptTheSessionIn(string headerValue)
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var wrongValue = new Dictionary<string, string>
            {
                [Consts.MCP.Server.Headers.SkillMetaClient] = headerValue
            };

            var session = await host.HandshakeAsync(client, "skill-meta-ordinal", initializeHeaders: wrongValue);
            AssertMatrixCell(await host.CallAsync(client, session, "tools/list", id: 2),
                expectSkillKeys: false, cell: $"initialize header value {headerValue.Replace(" ", "<sp>")}");

            // Positive control on the SAME host, SAME hub, SAME fixture: only the header VALUE
            // differs. Without it, every row above would also hold for a server that had stopped
            // reading the header at the session site altogether.
            var optedIn = await host.HandshakeAsync(client, "skill-meta-ordinal", initializeHeaders: SkillMetaOptIn);
            AssertMatrixCell(await host.CallAsync(client, optedIn, "tools/list", id: 3),
                expectSkillKeys: true, cell: "initialize header value 1 (positive control)");
        }

        // ─────────────────────────────────────────────────────────────────────
        // oauth mode — does authentication reorder anything?
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The same matrix under <c>auth=oauth</c>. This is the case that could have turned a
        /// lucky-but-working ordering into a silent failure: <c>UseAuthentication</c> and
        /// <c>UseAuthorization</c> bracket <see cref="Auth.McpSessionTokenMiddleware"/>, and the MCP
        /// endpoint carries <c>RequireAuthorization</c>. Measured, not assumed.
        /// </summary>
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public async Task ToolsList_SkillMetadata_HasTheSameSessionSemanticsUnderOAuth(
            bool headerOnInitialize, bool headerOnToolsList, bool expectSkillKeys)
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await OAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "skill-meta-oauth-semantics",
                initializeHeaders: headerOnInitialize ? SkillMetaOptIn : null);

            var body = await host.CallAsync(client, sessionId, "tools/list", id: 2,
                headers: headerOnToolsList ? SkillMetaOptIn : null);

            AssertMatrixCell(body, expectSkillKeys,
                $"oauth: initialize={headerOnInitialize}, tools/list={headerOnToolsList}",
                expectServerNativeTools: true);

            // The identity really was resolved, i.e. this ran as an AUTHENTICATED session and not as
            // an anonymous one that happened to be accepted. Without it, "oauth behaves the same"
            // could be satisfied by a host that never authenticated anything.
            hub.LastListSnapshot.AccountId.ShouldBe(host.AccountId,
                $"the session must be authenticated; got {hub.LastListSnapshot}");
        }

        // ─────────────────────────────────────────────────────────────────────
        // the sibling axis on the same ambient context
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>X-McpPlugin-Internal-Client</c> shares the ambient context and therefore the same
        /// session semantics. It is pinned here because the audit that accompanies this change
        /// classifies it as the identical case, and an unpinned classification is a claim, not a
        /// fact.
        ///
        /// <para>The observable is catalog VISIBILITY (the disabled tool), not <c>_meta</c>, so this
        /// case cannot pass or fail for the skill gate's reasons.</para>
        /// </summary>
        [Fact]
        public async Task TrustedInternalClientOptIn_HasTheSameSessionSemantics()
        {
            var hub = new DisabledToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            // Header on tools/list only → ignored, so the disabled tool stays hidden.
            var ignored = await host.HandshakeAsync(client, "trusted-session-semantics");
            ToolNames(await host.CallAsync(client, ignored, "tools/list", id: 2, headers: TrustedOptIn))
                .ShouldBe(new[] { DisabledToolHub.EnabledToolName },
                    "the trusted opt-in on a later request must not widen the catalog");

            // Header on initialize → honoured for the whole session, even on a listing that omits it.
            var honoured = await host.HandshakeAsync(client, "trusted-session-semantics", initializeHeaders: TrustedOptIn);
            ToolNames(await host.CallAsync(client, honoured, "tools/list", id: 3))
                .ShouldBe(new[] { DisabledToolHub.EnabledToolName, DisabledToolHub.DisabledToolName },
                    "the trusted opt-in taken at initialize governs the whole session");
        }

        // ───────────────────────────── assertions ─────────────────────────────

        /// <summary>
        /// One matrix cell's whole contract: the listing arrived, it carries BOTH fixture tools, the
        /// attributed tool's <c>_meta</c> matches the expectation exactly, and the unattributed tool
        /// never carries skill keys.
        /// </summary>
        static void AssertMatrixCell(string body, bool expectSkillKeys, string cell, bool expectServerNativeTools = false)
        {
            using var doc = JsonDocument.Parse(body);

            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse(
                $"[{cell}] tools/list must not fail; got: {body}");

            var tools = doc.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
            var names = tools.Select(t => t.GetProperty("name").GetString()).ToList();

            // Named, not counted: the whole point of the negative rows is "tool PRESENT, metadata
            // ABSENT", so a listing that lost the tool must not be able to satisfy them.
            names.ShouldContain(AmbientCaptureToolHub.AttributedToolName, $"[{cell}] got: {body}");
            names.ShouldContain(AmbientCaptureToolHub.UnattributedToolName, $"[{cell}] got: {body}");
            if (!expectServerNativeTools)
            {
                names.ShouldBe(
                    new List<string?> { AmbientCaptureToolHub.AttributedToolName, AmbientCaptureToolHub.UnattributedToolName },
                    $"[{cell}] the listing must be exactly the fixture catalog; got: {body}");
            }

            var attributed = tools.Single(t => t.GetProperty("name").GetString() == AmbientCaptureToolHub.AttributedToolName);
            var unattributed = tools.Single(t => t.GetProperty("name").GetString() == AmbientCaptureToolHub.UnattributedToolName);

            if (expectSkillKeys)
            {
                attributed.TryGetProperty("_meta", out var meta).ShouldBeTrue($"[{cell}] got: {body}");
                // Exact key SET and exact TEXT: an empty object, a truncated body, or an extra key
                // all fail. A presence claim that only checked "has _meta" could be satisfied by
                // `{"enabled":false}`.
                meta.EnumerateObject().Select(p => p.Name).ShouldBe(
                    new[] { ExtensionsListMeta.SkillDescriptionKey, ExtensionsListMeta.SkillBodyKey },
                    $"[{cell}] got: {body}");
                meta.GetProperty(ExtensionsListMeta.SkillDescriptionKey).GetString()
                    .ShouldBe(AmbientCaptureToolHub.SkillDescriptionText, $"[{cell}] got: {body}");
                meta.GetProperty(ExtensionsListMeta.SkillBodyKey).GetString()
                    .ShouldBe(AmbientCaptureToolHub.SkillBodyText, $"[{cell}] got: {body}");
            }
            else
            {
                // The pre-skill-metadata wire shape for an ENABLED tool is NO `_meta` at all — the
                // whole payload, not "the skill keys are missing". A partially built object fails.
                attributed.TryGetProperty("_meta", out var meta).ShouldBeFalse(
                    $"[{cell}] an enabled tool must carry no _meta for a caller that did not opt in; got: {body}");
            }

            // In-cell control, both directions: the tool that declares nothing never gains keys,
            // even in a cell where the session DID opt in. This is what proves the keys are
            // attribute-driven rather than sprayed over the catalog.
            unattributed.TryGetProperty("_meta", out _).ShouldBeFalse(
                $"[{cell}] a tool with no skill attributes must never carry _meta; got: {body}");
        }

        static IReadOnlyList<string?> ToolNames(string body)
        {
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse($"tools/list must not fail; got: {body}");
            return doc.RootElement.GetProperty("result").GetProperty("tools")
                .EnumerateArray()
                .Select(t => t.GetProperty("name").GetString())
                .ToList();
        }
    }
}
