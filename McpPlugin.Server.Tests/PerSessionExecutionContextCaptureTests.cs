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
using System.Net.Http;
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
    /// MEASURES the mechanism every session-scoped ambient value on the streamable-HTTP MCP path
    /// depends on: <c>HttpServerTransportOptions.PerSessionExecutionContext</c> is <c>true</c>
    /// (<c>StreamableHttpTransportLayer</c>), so an MCP request handler runs on the
    /// <see cref="System.Threading.ExecutionContext"/> captured when <c>RunSessionHandler</c> was
    /// invoked — during <c>initialize</c> — and NOT on the context of the HTTP request it is
    /// answering.
    ///
    /// <para>The measurement is a four-way discrimination rather than an argument. Each request of
    /// one session carries a DIFFERENT <c>User-Agent</c>;
    /// <see cref="Auth.McpSessionTokenMiddleware"/> copies it into the ambient
    /// <c>CurrentUserAgent</c> on every request, so the value a handler observes names the request
    /// whose <c>ExecutionContext</c> it is running on: the <c>initialize</c> POST, the
    /// <c>notifications/initialized</c> POST, its own POST, or none of them. Only one of those four
    /// outcomes is consistent with each competing explanation, so the result cannot be read both
    /// ways.</para>
    ///
    /// <para><c>User-Agent</c> is used deliberately instead of a purpose-built probe: it is an
    /// EXISTING production ambient value written by the real middleware on the real path, so the
    /// measurement adds no instrumentation of its own that could itself be the thing being
    /// measured. The snapshot is taken from inside <see cref="AmbientCaptureToolHub"/>, which the
    /// MCP router awaits — the same frames <c>ExtensionsListMeta.BuildToolMeta</c> runs in.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PerSessionExecutionContextCaptureTests
    {
        const string UserAgentHeader = "User-Agent";

        const string InitializeUserAgent = "measure-initialize/1.0";
        const string InitializedUserAgent = "measure-notifications-initialized/1.0";
        const string ToolsListUserAgent = "measure-tools-list/1.0";
        const string ToolsCallUserAgent = "measure-tools-call/1.0";

        /// <summary>
        /// The core measurement. A <c>tools/list</c> handler observes the ambient state of the
        /// <c>initialize</c> request — not its own, and not the most recent one.
        /// </summary>
        [Fact]
        public async Task ToolsListHandler_ObservesTheAmbientStateOfTheInitializeRequest()
        {
            var hub = new AmbientCaptureToolHub();
            var snapshot = await MeasureAsync(hub);

            // Confirms: the handler ran on the initialize request's ExecutionContext.
            snapshot.UserAgent.ShouldBe(InitializeUserAgent,
                $"handlers must observe the initialize request's ambient state; got {snapshot}");

            // ...and the three rival readings are each excluded by name, so a future change that
            // makes handlers follow the request context reddens here with a message that says WHICH
            // request it followed instead of only that the expectation moved.
            snapshot.UserAgent.ShouldNotBe(ToolsListUserAgent,
                "a handler observing its OWN request would mean PerSessionExecutionContext is no longer in effect");
            snapshot.UserAgent.ShouldNotBe(InitializedUserAgent,
                "a handler observing the PREVIOUS request would mean the context is re-captured per request");
            snapshot.UserAgent.ShouldNotBeNull(
                "a null would mean the ambient value never reached the handler at all");
        }

        /// <summary>
        /// The same measurement stated on the flag this taskflow is about, so the mechanism above
        /// and the behaviour under test are pinned by the same instrument.
        ///
        /// <para>The header rides on <c>tools/list</c> ONLY. If the opt-in were a property of the
        /// request, the handler would see <c>true</c>; it sees the <c>initialize</c> request's
        /// value, which is <c>false</c>.</para>
        /// </summary>
        [Fact]
        public async Task SkillMetaFlag_InsideTheHandler_ReflectsTheInitializeRequest_NotTheCurrentOne()
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "ec-capture-flag-test");

            await host.CallAsync(client, sessionId, "tools/list", id: 2, headers: new Dictionary<string, string>
            {
                [Consts.MCP.Server.Headers.SkillMetaClient] = Consts.MCP.Server.Headers.SkillMetaClientOptInValue,
                [Consts.MCP.Server.Headers.TrustedInternalClient] = Consts.MCP.Server.Headers.TrustedInternalClientOptInValue
            });

            var observed = hub.LastListSnapshot;
            observed.IsSkillMetaClient.ShouldBeFalse(
                $"the tools/list request's own opt-in header does not reach the handler; got {observed}");
            observed.IsTrustedInternalClient.ShouldBeFalse(
                $"the same holds for the sibling flag on the same ambient context; got {observed}");

            // Positive control on the SAME host and the SAME hub: with the header on `initialize`,
            // both flags DO reach the handler. Without this, the two falses above would be equally
            // satisfied by a request that never reached the plugin seam, or by a server that had
            // simply forgotten how to read either header.
            var optedInSession = await host.HandshakeAsync(client, "ec-capture-flag-test", initializeHeaders: new Dictionary<string, string>
            {
                [Consts.MCP.Server.Headers.SkillMetaClient] = Consts.MCP.Server.Headers.SkillMetaClientOptInValue,
                [Consts.MCP.Server.Headers.TrustedInternalClient] = Consts.MCP.Server.Headers.TrustedInternalClientOptInValue
            });
            await host.CallAsync(client, optedInSession, "tools/list", id: 3);

            var optedIn = hub.LastListSnapshot;
            optedIn.IsSkillMetaClient.ShouldBeTrue(
                $"the initialize request's opt-in header IS what the handler observes; got {optedIn}");
            optedIn.IsTrustedInternalClient.ShouldBeTrue($"same for the sibling flag; got {optedIn}");
        }

        /// <summary>
        /// Rules <c>tools/call</c> IN: it is served by the same session context, so it inherits the
        /// same frozen ambient state. This is measured rather than assumed — <c>tools/call</c> is a
        /// different SDK handler and could in principle have been dispatched differently.
        /// </summary>
        [Fact]
        public async Task ToolsCallHandler_AlsoObservesTheAmbientStateOfTheInitializeRequest()
        {
            var hub = new AmbientCaptureToolHub();
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "ec-capture-call-test",
                initializeHeaders: new Dictionary<string, string> { [UserAgentHeader] = InitializeUserAgent },
                initializedHeaders: new Dictionary<string, string> { [UserAgentHeader] = InitializedUserAgent });

            await host.PostAsync(client, sessionId, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = AmbientCaptureToolHub.AttributedToolName, arguments = new { } }
            }, new Dictionary<string, string> { [UserAgentHeader] = ToolsCallUserAgent });

            hub.CallSnapshots.Count.ShouldBe(1, "the call must have reached the plugin seam");
            var snapshot = hub.CallSnapshots[0];
            snapshot.UserAgent.ShouldBe(InitializeUserAgent,
                $"tools/call runs on the same captured session context as tools/list; got {snapshot}");
            snapshot.UserAgent.ShouldNotBe(ToolsCallUserAgent);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static async Task<AmbientSnapshot> MeasureAsync(AmbientCaptureToolHub hub)
        {
            await using var host = await NoneAuthMcpHost.StartAsync(s => s.AddSingleton<IClientToolHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, "ec-capture-test",
                initializeHeaders: new Dictionary<string, string> { [UserAgentHeader] = InitializeUserAgent },
                initializedHeaders: new Dictionary<string, string> { [UserAgentHeader] = InitializedUserAgent });

            await host.CallAsync(client, sessionId, "tools/list", id: 2,
                headers: new Dictionary<string, string> { [UserAgentHeader] = ToolsListUserAgent });

            hub.ListSnapshots.Count.ShouldBe(1, "tools/list must have reached the plugin seam exactly once");
            return hub.LastListSnapshot;
        }
    }
}
