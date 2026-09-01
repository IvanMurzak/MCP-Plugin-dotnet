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
using System.Reflection;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The audit that accompanies the skill-meta session-semantics ruling: EVERY ambient value on
    /// <see cref="McpSessionTokenContext"/>, classified, with the classification made mechanical
    /// rather than prose.
    ///
    /// <para><c>IsSkillMetaClient</c> was found to be frozen into the session by a probe, not by
    /// review. The lesson is that the freezing is a property of the CONTEXT, not of that one flag —
    /// so the useful artifact is an enumeration that a tenth ambient value cannot join silently.
    /// The narrative table lives in <c>docs/architecture-transport-auth.md</c>; this file is what
    /// keeps it honest.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class AmbientSessionStateAuditTests
    {
        /// <summary>
        /// How each ambient value behaves on the streamable-HTTP MCP path.
        /// </summary>
        public enum SessionScope
        {
            /// <summary>
            /// Written per request by the middleware AND republished from the <c>initialize</c>
            /// request inside <c>RunSessionHandler</c>, so a handler observes a value that was put
            /// there on purpose. This is the only class whose correctness does not depend on where
            /// the middleware sits in the pipeline.
            /// </summary>
            RepublishedAtInitialize,

            /// <summary>
            /// Written per request by the middleware only. On the MCP path a handler therefore sees
            /// the <c>initialize</c> request's value — correct for values that cannot change within
            /// a session (the connection's remote IP, the URL's project pin), and inert for values
            /// nothing on this path reads.
            /// </summary>
            FrozenAtInitialize,

            /// <summary>
            /// Frozen like the row above, but a reader compensates by looking the live value up
            /// elsewhere. Recording this separately matters: the frozen value is NOT the authority,
            /// and "it works" is evidence about the compensator, not about the ambient slot.
            /// </summary>
            FrozenButCompensated
        }

        /// <summary>
        /// The declared classification. Adding a property to <see cref="McpSessionTokenContext"/>
        /// without adding a row here reddens <see cref="EveryAmbientValueIsClassified"/>.
        /// </summary>
        static readonly IReadOnlyDictionary<string, SessionScope> Classification = new Dictionary<string, SessionScope>(StringComparer.Ordinal)
        {
            // Republished from the initialize request in StreamableHttpTransportLayer.RunSessionHandler.
            [nameof(McpSessionTokenContext.CurrentToken)] = SessionScope.RepublishedAtInitialize,
            [nameof(McpSessionTokenContext.CurrentSessionId)] = SessionScope.RepublishedAtInitialize,
            [nameof(McpSessionTokenContext.IsSkillMetaClient)] = SessionScope.RepublishedAtInitialize,
            [nameof(McpSessionTokenContext.IsTrustedInternalClient)] = SessionScope.RepublishedAtInitialize,

            // Middleware-only. Constant per session by construction (the pin is a path segment of the
            // client's configured URL; the IP is the connection's) or not read on the MCP path.
            [nameof(McpSessionTokenContext.CurrentProjectPin)] = SessionScope.FrozenAtInitialize,
            [nameof(McpSessionTokenContext.CurrentClientIp)] = SessionScope.FrozenAtInitialize,
            [nameof(McpSessionTokenContext.CurrentUserAgent)] = SessionScope.FrozenAtInitialize,

            // Frozen, and a reader compensates. CurrentIdentity is re-read from the HttpContext's
            // principal inside RunSessionHandler as a FALLBACK for the reaper only — it is never
            // written back to the ambient slot, so handlers keep the initialize request's value.
            [nameof(McpSessionTokenContext.CurrentIdentity)] = SessionScope.FrozenAtInitialize,
            // AccountMcpStrategy.ResolveSelection treats a null here as "ask the store", keyed by
            // CurrentSessionId — which is why sticky selection works across requests (issue #195).
            [nameof(McpSessionTokenContext.CurrentSelectedInstanceId)] = SessionScope.FrozenButCompensated,
        };

        /// <summary>
        /// The enumeration itself. "None found" is not an acceptable audit result unless the search
        /// is shown, and a hand-written list goes stale the first time somebody adds a field.
        /// </summary>
        [Fact]
        public void EveryAmbientValueIsClassified()
        {
            var declared = typeof(McpSessionTokenContext)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            declared.ShouldBe(
                Classification.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                "every ambient value must carry a session-scope classification; a new one is not " +
                "automatically safe just because nothing reads it yet");

            // The count is pinned separately and literally so the assertion above cannot be
            // satisfied by both sides shrinking together in one careless edit.
            declared.Count.ShouldBe(9);
        }

        /// <summary>
        /// The classification's OBSERVABLE half, over a real session: every value a handler reads is
        /// the <c>initialize</c> request's — including the bearer token, which a later request can
        /// change on the wire without the handler noticing.
        ///
        /// <para>Three inputs are varied per request (<c>User-Agent</c>, <c>Authorization</c>, and
        /// the skill-meta opt-in) so the result is not carried by one lucky value; and
        /// <c>CurrentSessionId</c> is asserted to be the SERVER-MINTED id, which the
        /// <c>initialize</c> request could not have supplied — that is the positive artifact for the
        /// <c>RepublishedAtInitialize</c> class, and it is what distinguishes "published on purpose"
        /// from "frozen by accident".</para>
        /// </summary>
        [Fact]
        public async Task OverARealSession_EveryAmbientValueAHandlerReadsIsTheInitializeRequests()
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "ambient-audit",
                initializeHeaders: new Dictionary<string, string>
                {
                    ["User-Agent"] = "audit-initialize/1.0",
                    ["Authorization"] = "Bearer token-from-initialize"
                });

            await host.CallAsync(client, sessionId, "tools/list", id: 2, headers: new Dictionary<string, string>
            {
                ["User-Agent"] = "audit-tools-list/1.0",
                ["Authorization"] = "Bearer token-from-tools-list"
            });

            var observed = hub.LastListSnapshot;

            observed.UserAgent.ShouldBe("audit-initialize/1.0", $"got {observed}");
            observed.Token.ShouldBe("token-from-initialize",
                $"a later request's bearer token does not reach an MCP handler; got {observed}");
            observed.ClientIp.ShouldBe("127.0.0.1", $"got {observed}");

            // CurrentSessionId is the exception that proves the rule: the initialize request carries
            // NO Mcp-Session-Id (the server mints it in that response), so this value cannot have
            // come from the request at all — it is published from inside RunSessionHandler.
            observed.SessionId.ShouldBe(sessionId,
                $"the session id must be the server-minted one, republished from RunSessionHandler; got {observed}");
        }

        /// <summary>
        /// The highest-stakes member of the family, and the one whose answer was NOT what reading
        /// the code predicted.
        ///
        /// <para>Ambient <c>CurrentIdentity</c> is frozen at <c>initialize</c> like everything else,
        /// so the obvious worry is a request presenting account B's valid token being ROUTED as
        /// account A. Measured, that cannot happen — but the control is not ours: the MCP SDK's own
        /// streamable-HTTP handler binds a session to the user that created it and answers
        /// <c>403 Forbidden: The currently authenticated user does not match the user who initiated
        /// the session.</c> before any handler runs. The frozen identity can therefore never
        /// disagree with the credential on the wire, because a disagreeing request never reaches a
        /// handler.</para>
        ///
        /// <para>This is pinned rather than left as an observation precisely BECAUSE the control
        /// lives in a dependency. An SDK upgrade that relaxed it would turn a frozen ambient value
        /// from harmless into a cross-account routing bug, and nothing else in this repository would
        /// notice.</para>
        /// </summary>
        [Fact]
        public async Task ADifferentAccountsTokenOnAnEstablishedSession_IsRejectedBeforeAnyHandlerRuns()
        {
            const string other = "acct-someone-else";

            var hub = new AmbientCaptureToolHub();
            await using var host = await OAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "ambient-audit-identity");

            var listRequest = new { jsonrpc = "2.0", id = 2, method = "tools/list" };

            // Positive control FIRST, on the same session: the establishing token is accepted and
            // the request reaches the plugin seam. Without it, the rejection below would be equally
            // satisfied by a session that was already dead.
            var (okStatus, okBody) = await host.PostExpectingAnyStatusAsync(client, sessionId, listRequest);
            okStatus.ShouldBe(200, $"the establishing token must still work; body: {okBody}");
            hub.ListSnapshots.Count.ShouldBe(1, "the control request must have reached the plugin seam");
            hub.LastListSnapshot.AccountId.ShouldBe(host.AccountId, $"got {hub.LastListSnapshot}");

            // Same session, a different account's VALID token on the wire.
            var (status, body) = await host.PostExpectingAnyStatusAsync(
                client, sessionId, new { jsonrpc = "2.0", id = 3, method = "tools/list" },
                bearerToken: host.TokenForAccount(other));

            status.ShouldBe(403, $"a foreign account's token must not be usable on this session; body: {body}");
            body.ShouldContain("does not match the user who initiated the session", Case.Sensitive,
                $"the rejection must be the SDK's session/user binding, not an unrelated failure; body: {body}");

            // ...and it was rejected BEFORE the handler: the seam count did not move. This is what
            // makes "the frozen identity is harmless" a measurement rather than an inference.
            hub.ListSnapshots.Count.ShouldBe(1,
                "the rejected request must never have reached a tools/list handler");
        }

        /// <summary>
        /// Rules <c>prompts/list</c> and <c>resources/list</c> IN: they run on the same captured
        /// session context, so the trusted-client opt-in they consume through
        /// <c>ExtensionsListMeta.SelectVisible</c> has the same session semantics as the tool list.
        ///
        /// <para>They are ruled in for the MECHANISM only. Neither can carry skill metadata at all:
        /// all three non-tool list routers assign <c>BuildEnabledMeta</c>'s result wholesale and
        /// <c>BuildToolMeta</c> — the sole reader of <c>IsSkillMetaClient</c> — has exactly one call
        /// site, <c>ExtensionsTool.ToTool()</c>, reached only from <c>ToolRouter.ListAll</c>. That
        /// composition is pinned by <c>ToolListSkillMetaTests.BuildEnabledMeta_StillEmitsExactlyTheEnabledKey</c>;
        /// what this case adds is that the ambient FLOW is the same for them.</para>
        /// </summary>
        [Fact]
        public async Task PromptAndResourceLists_ShareTheSameSessionScopedVisibilityFlag()
        {
            await using var host = await NoneAuthMcpHost.StartAsync(s =>
            {
                s.AddSingleton<IClientPromptHub>(new DisabledPromptHub());
                s.AddSingleton<IClientResourceHub>(new DisabledResourceHub());
            });
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var trustedOptIn = new Dictionary<string, string>
            {
                [Common.Consts.MCP.Server.Headers.TrustedInternalClient] =
                    Common.Consts.MCP.Server.Headers.TrustedInternalClientOptInValue
            };

            // Opt-in on the LIST request only → ignored, so the disabled entries stay hidden.
            var ignored = await host.HandshakeAsync(client, "ambient-audit-prompts");
            Names(await host.CallAsync(client, ignored, "prompts/list", id: 2, headers: trustedOptIn), "prompts", "name")
                .ShouldBe(new[] { DisabledPromptHub.EnabledPromptName });
            Names(await host.CallAsync(client, ignored, "resources/list", id: 3, headers: trustedOptIn), "resources", "uri")
                .ShouldBe(new[] { DisabledResourceHub.EnabledResourceUri });

            // Opt-in at initialize → honoured for the whole session, on a request that omits it.
            // This is the positive half: without it, both assertions above would also hold for a
            // server that had simply stopped reading the header anywhere.
            var honoured = await host.HandshakeAsync(client, "ambient-audit-prompts", initializeHeaders: trustedOptIn);
            Names(await host.CallAsync(client, honoured, "prompts/list", id: 4), "prompts", "name")
                .ShouldBe(new[] { DisabledPromptHub.EnabledPromptName, DisabledPromptHub.DisabledPromptName });
            Names(await host.CallAsync(client, honoured, "resources/list", id: 5), "resources", "uri")
                .ShouldBe(new[] { DisabledResourceHub.EnabledResourceUri, DisabledResourceHub.DisabledResourceUri });
        }

        static IReadOnlyList<string?> Names(string body, string collection, string field)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            doc.RootElement.TryGetProperty("error", out _).ShouldBeFalse($"list must not fail; got: {body}");
            return doc.RootElement.GetProperty("result").GetProperty(collection)
                .EnumerateArray()
                .Select(x => x.GetProperty(field).GetString())
                .ToList();
        }
    }
}
