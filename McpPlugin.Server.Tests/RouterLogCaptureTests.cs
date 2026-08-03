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
using NLog;
using NLog.Config;
using Shouldly;
using Xunit;
using NLogLevel = NLog.LogLevel;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Cover for <see cref="CapturedRouterLogs"/> — the shared way this assembly asserts on what a
    /// router told the operator — and for the second consumer that proves it is genuinely shared.
    ///
    /// <para>Why this matters more than a normal helper test: an operator-visibility requirement
    /// ("the operator must still see a warning") used to be UNTESTABLE here. The routers log through
    /// NLog's static <c>LogManager</c>, so the suite could not observe them, and deleting a router's
    /// <c>Warn</c> line left all ~676 server tests green. A helper that silently captured nothing
    /// would restore exactly that failure mode while LOOKING like cover, so the cases below assert
    /// the capture's POSITIVE artifacts (what it did record, and that it really did install) rather
    /// than only what it did not record.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class RouterLogCaptureTests
    {
        /// <summary>The text the fake plugin call fails with — the operator's only copy of the cause.</summary>
        const string PluginFailureText =
            "Invoke 'RunListResources': Failed to invoke " +
            "'com.IvanMurzak.McpPlugin.Common.Model.RequestListResources' after 10 retries.";

        /// <summary>The client name this fixture family handshakes with.</summary>
        const string ClientName = "resource-list-log-test";

        /// <summary>
        /// Asks the gate without waiting: <c>null</c> means another capture still holds it. The zero
        /// timeout is what keeps the gate cases deterministic — no sleeps, no threads, no timing
        /// window — so it is spelled once here rather than at each call site.
        /// </summary>
        static CapturedRouterLogs? TryInstallWithoutWaiting(Type routerType)
            => CapturedRouterLogs.TryInstallFor(routerType, NLogLevel.Warn, TimeSpan.Zero);

        // ─────────────────── a SECOND router, through the shared helper ───────────────────

        /// <summary>
        /// The degraded <c>resources/list</c> path returns an empty catalog to the client, so the
        /// router's <c>Warn</c> is the only place the cause survives. Asserted as a PRESENCE claim,
        /// which an empty capture cannot satisfy, and paired with the hub's call count so the text
        /// is provably the log of a call that reached the plugin seam and failed there.
        ///
        /// <para>This case exists on a DIFFERENT router from the prompt one on purpose: it is what
        /// makes <see cref="CapturedRouterLogs"/> a shared facility rather than one test's local
        /// workaround.</para>
        /// </summary>
        [Fact]
        public async Task ResourcesList_WhenThePluginCallFails_LogsTheFailureTextForOperators()
        {
            var hub = FakeResourceHub.ThatFailsWith(PluginFailureText);

            using var logs = CapturedRouterLogs.InstallFor(typeof(ResourceRouter));

            await using var host = await NoneAuthMcpHost.StartAsync(services => services.AddSingleton<IClientResourceHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, ClientName);
            var body = await host.CallAsync(client, sessionId, "resources/list");

            hub.ListCallCount.ShouldBe(1, "the router must have called the plugin hub exactly once");

            logs.Text.ShouldContain(PluginFailureText, Case.Sensitive,
                $"the degraded resources/list must still tell the operator why; wire body was: {body}");
        }

        /// <summary>
        /// The paired half, and the reason the case above is not vacuous: on the SUCCESS path the
        /// same host and the same seam put the resources on the wire (a positive artifact — this
        /// fixture can emit) and the operator log stays clean at Warn. Together the pair pins both
        /// directions: the warning appears exactly when the plugin call fails, and not otherwise.
        /// </summary>
        [Fact]
        public async Task ResourcesList_WhenThePluginReturnsResources_EmitsThemOverTheWireAndLogsNothingAtWarn()
        {
            var hub = FakeResourceHub.ThatReturns(
                new ResponseListResource("res://alpha", "alpha"),
                new ResponseListResource("res://beta", "beta"));

            using var logs = CapturedRouterLogs.InstallFor(typeof(ResourceRouter));

            await using var host = await NoneAuthMcpHost.StartAsync(services => services.AddSingleton<IClientResourceHub>(hub));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

            var sessionId = await host.HandshakeAsync(client, ClientName);
            var body = await host.CallAsync(client, sessionId, "resources/list");

            using var doc = JsonDocument.Parse(body);

            var uris = doc.RootElement.GetProperty("result").GetProperty("resources")
                .EnumerateArray()
                .Select(r => r.GetProperty("uri").GetString())
                .ToList();

            uris.ShouldBe(new List<string?> { "res://alpha", "res://beta" },
                $"the plugin's resources must reach the client verbatim and in order; got: {body}");

            logs.Lines.ShouldBeEmpty(
                "a successful resources/list must not warn the operator from ResourceRouter");
        }

        // ─────────────────── the helper's own guarantees ───────────────────

        /// <summary>
        /// The capture is scoped to ONE router's logger, which is what lets it coexist with the test
        /// classes in this assembly that carry no <c>[Collection]</c> and therefore really do run
        /// concurrently. Both directions are asserted from the same capture, so neither half can pass
        /// because nothing was logged at all: the requested router's line MUST be there, and the
        /// other router's MUST NOT.
        /// </summary>
        [Fact]
        public void Capture_IsScopedToTheRequestedRouter()
        {
            const string Wanted = "wanted-line-from-the-prompt-router";
            const string Unwanted = "unwanted-line-from-the-resource-router";

            using var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));

            // Written through the very logger names the routers use: LogManager.GetCurrentClassLogger()
            // names a logger after its declaring type.
            LogManager.GetLogger(typeof(PromptRouter).FullName!).Warn(Wanted);
            LogManager.GetLogger(typeof(ResourceRouter).FullName!).Warn(Unwanted);

            logs.Text.ShouldContain(Wanted, Case.Sensitive,
                "the capture must record what the router it was installed for logged");
            logs.Text.ShouldNotContain(Unwanted, Case.Sensitive,
                "the capture must not record another router's output, or concurrent tests would pollute it");
        }

        /// <summary>
        /// Below the Warn floor nothing is captured, so an "operator-visible" assertion cannot be
        /// satisfied by a Debug/Info line that no operator would see at default verbosity. Paired
        /// with a Warn written through the same logger, so the emptiness cannot come from a capture
        /// that records nothing at all.
        /// </summary>
        [Fact]
        public void Capture_TakesWarnAndAbove_NotChatter()
        {
            using var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));

            var logger = LogManager.GetLogger(typeof(PromptRouter).FullName!);
            logger.Info("info-chatter");
            logger.Debug("debug-chatter");
            logger.Warn("operator-visible-line");

            logs.Lines.ShouldHaveSingleItem().ShouldBe("operator-visible-line");
        }

        /// <summary>
        /// Disposal puts back the EXACT <see cref="LoggingConfiguration"/> instance that was installed
        /// before, so a capture cannot leak into whatever runs next. A sentinel configuration is
        /// installed first rather than asserting against whatever NLog happened to hold, because the
        /// interesting claim is identity, and identity needs a known instance to compare against.
        /// </summary>
        [Fact]
        public void Dispose_RestoresTheExactPreviousNLogConfiguration()
        {
            var original = LogManager.Configuration;
            var sentinel = new LoggingConfiguration();

            try
            {
                // This sentinel install and its restore in the finally below are the only writes to
                // LogManager.Configuration outside the helper, and neither holds the helper's gate —
                // they cannot, since the point is to plant a known instance for the capture to find.
                // Safe because both capture-installing classes carry [Collection("McpPlugin.Server")],
                // so nothing can be mid-capture here. A third writer, or a capture-installing class
                // without that attribute, breaks that assumption — move these under the gate then.
                LogManager.Configuration = sentinel;

                using (CapturedRouterLogs.InstallFor(typeof(PromptRouter)))
                {
                    // Positive artifact: a helper that installed nothing at all would leave `sentinel`
                    // in place and fail here. That the replacement also ROUTES the router's logger
                    // into the capture is a separate claim, pinned by
                    // Capture_IsScopedToTheRequestedRouter.
                    LogManager.Configuration.ShouldNotBeSameAs(sentinel,
                        "installing a capture must replace the active NLog configuration");
                }

                LogManager.Configuration.ShouldBeSameAs(sentinel,
                    "Dispose must put back the exact configuration instance it found");
            }
            finally
            {
                LogManager.Configuration = original;
            }
        }

        /// <summary>
        /// The mutual-exclusion guarantee, stated as a test rather than as a comment: while one
        /// capture is installed, a second cannot be. This is what makes the whole-configuration swap
        /// safe in an assembly where roughly a third of the test classes carry no
        /// <c>[Collection]</c> and run in parallel — two overlapping captures would drop each other's
        /// rules and restore in the wrong order.
        ///
        /// <para>The zero timeout keeps this deterministic: no sleeps, no threads, no timing window.
        /// The second half is what stops the gate from passing this test by simply being permanently
        /// shut.</para>
        /// </summary>
        [Fact]
        public void Install_WhileAnotherCaptureIsHeld_IsRefusedUntilItIsDisposed()
        {
            using (CapturedRouterLogs.InstallFor(typeof(PromptRouter)))
            {
                var refused = TryInstallWithoutWaiting(typeof(ResourceRouter));
                try
                {
                    refused.ShouldBeNull(
                        "a second capture must not be admitted while the first one still owns LogManager.Configuration");
                }
                finally
                {
                    refused?.Dispose();
                }
            }

            using var admitted = TryInstallWithoutWaiting(typeof(ResourceRouter));
            admitted.ShouldNotBeNull(
                "the gate must open again once the previous capture is disposed, or every later capture would time out");
        }

        /// <summary>
        /// Disposal is idempotent — a guarantee <see cref="CapturedRouterLogs"/> folds into its
        /// exact-restore mechanism, and the one thing nothing else in this assembly exercises: no
        /// consumer disposes a capture twice, so without this case the guard could be deleted and
        /// every test would stay green.
        ///
        /// <para>Two claims, and each needs the other. A second <c>Dispose</c> must not throw: the
        /// gate is a <c>SemaphoreSlim(1, 1)</c>, so releasing it twice raises
        /// <c>SemaphoreFullException</c> — out of a <c>using</c> block's implicit disposal, where it
        /// masks whatever the test was really asserting. And the gate must still ADMIT a capture
        /// afterwards, which is what stops the first claim being satisfied by a <c>Dispose</c> that
        /// does nothing at all (that one never releases the gate, so the admission below fails).</para>
        /// </summary>
        [Fact]
        public void Dispose_IsIdempotent_AndLeavesTheGateOpen()
        {
            var logs = CapturedRouterLogs.InstallFor(typeof(PromptRouter));
            logs.Dispose();

            Should.NotThrow(() => logs.Dispose());

            using var admitted = TryInstallWithoutWaiting(typeof(PromptRouter));
            admitted.ShouldNotBeNull(
                "a double Dispose must leave the gate exactly as one Dispose does — still open for the next capture");
        }

        // ───────────────────────────── fake plugin hub ─────────────────────────────

        /// <summary>
        /// Stands in for <c>RemoteResourceRunner</c> so both outcomes of the plugin call are reachable
        /// without a live SignalR plugin: the real runner needs an attached engine plugin, and its
        /// failure path costs a full retry budget in wall-clock time.
        /// </summary>
        sealed class FakeResourceHub : IClientResourceHub
        {
            readonly ResponseData<ResponseListResource[]> _response;
            int _listCallCount;

            FakeResourceHub(ResponseData<ResponseListResource[]> response) => _response = response;

            public int ListCallCount => Volatile.Read(ref _listCallCount);

            public static FakeResourceHub ThatFailsWith(string message)
                => new FakeResourceHub(ResponseData<ResponseListResource[]>.Error("req-fail", message));

            // Packed exactly as the real producer packs it, so the success envelope this fixture feeds
            // the router carries the same Message the plugin would.
            public static FakeResourceHub ThatReturns(params ResponseListResource[] resources)
                => new FakeResourceHub(resources.Pack("req-ok"));

            public Task<ResponseData<ResponseListResource[]>> RunListResources(RequestListResources request, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _listCallCount);
                return _response.TaskFromResult();
            }

            public Task<ResponseData<ResponseResourceContent[]>> RunResourceContent(RequestResourceContent request)
                => throw new NotSupportedException("These tests only drive resources/list.");

            public Task<ResponseData<ResponseResourceTemplate[]>> RunResourceTemplates(RequestListResourceTemplates request, CancellationToken cancellationToken = default)
                => throw new NotSupportedException("These tests only drive resources/list.");
        }
    }
}
