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
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end cover for the server-side <b>replace-by-identity</b> rule (design 07 §2.3, D10.2).
    ///
    /// <para>These drive a REAL loopback Kestrel host with the REAL <c>WithMcpServer</c> +
    /// <c>WithMcpPluginServer</c> wiring over the REAL Streamable HTTP transport and the REAL OAuth
    /// resource-server path, and they reach the rule the only way production does: through an actual MCP
    /// <c>initialize</c> handshake carrying an actual <c>AI-Game-Dev-Instance-Id</c> header.</para>
    ///
    /// <para><b>Why a real host is not optional here.</b> The thing under test is what happens to a session
    /// the MCP SDK owns. Verified against <c>ModelContextProtocol.AspNetCore</c> 1.4.0: NOTHING in the SDK
    /// removes a session from its store when that session's <c>RunSessionHandler</c> returns, so a replace
    /// rule built on cancelling our own token would drop our tracker row, look fixed, and leave the SDK's
    /// session — and the memory it pins — in place until the 6 h idle timeout. A test that asserted only
    /// "the tracker row disappeared" would be GREEN for that broken implementation. So the load-bearing
    /// assertion in this file is the one no in-process fake can make: <b>a request bearing the replaced
    /// session id is answered 404 by the SDK itself</b>, which is only possible if the SDK's own session
    /// store no longer holds it.</para>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class McpReplaceByIdentityOverHttpTests
    {
        const string Kid = "replace-by-identity-kid";
        const string AccountA = "acct-replace-a";
        const string AccountB = "acct-replace-b";
        const string InstanceX = "0123456789abcdef0123456789abcdef";
        const string InstanceY = "fedcba9876543210fedcba9876543210";

        // ───────────────────────────── the mechanism (DoD 1) ─────────────────────────────

        /// <summary>
        /// The tripwire for an SDK bump. The eviction seam reaches an <c>internal</c> SDK type by
        /// reflection because 1.4.0 exposes no public or DI-typed way to evict a session by id. If a future
        /// bump moves or renames that seam, replace-by-identity silently stops terminating SDK sessions and
        /// the six-hour pile-up returns <b>with no functional symptom</b>. This test is what turns that
        /// silent regression into a red build.
        /// </summary>
        [Fact]
        public void SdkEvictionSeam_IsBoundToTheRunningSdk()
        {
            McpSdkSessionEvictor.IsBindingResolved.ShouldBeTrue(
                "replace-by-identity cannot terminate SDK-owned MCP sessions unless this seam binds. " +
                "Diagnostic: " + McpSdkSessionEvictor.StaticBindingDiagnostic);

            McpSdkSessionEvictor.StaticBindingDiagnostic.ShouldStartWith("BOUND:");
        }

        /// <summary>
        /// <b>The positive control, and the proof of termination.</b> A second connection with the same
        /// account AND the same instance id replaces the first: the old session is gone from the SDK's own
        /// store, the new one is live, and our tracker count falls back to one.
        /// </summary>
        [Fact]
        public async Task SecondConnection_SameAccountAndInstance_ReplacesTheFirst_AndTheSdkReleasesIt()
        {
            await using var host = await StartOAuthHostAsync();
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            var reaper = host.Services.GetRequiredService<IMcpSessionReaper>();
            using var client = NewClient();

            var first = await host.InitializeAsync(client, AccountA, InstanceX);
            first.ShouldNotBeNullOrEmpty("the server must mint an Mcp-Session-Id on initialize");
            tracker.ActiveSessionCount.ShouldBe(1);
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe(first);

            // Prove the first session is genuinely USABLE before it is replaced. Without this, a later
            // "the old id 404s" assertion could be satisfied by an id that never worked in the first place.
            var beforeReplace = await host.SendAsync(client, AccountA, first, InstanceX, Ping(id: 2));
            beforeReplace.Status.ShouldBe(HttpStatusCode.OK, "the first session must work before it is replaced");

            var second = await host.InitializeAsync(client, AccountA, InstanceX);
            second.ShouldNotBeNullOrEmpty();
            second.ShouldNotBe(first, "every initialize mints a fresh MCP session id — that is why the " +
                                     "session id alone can never be a replace key");

            // ── THE load-bearing assertion. 404 comes from the SDK's own session lookup, so it can only
            // ── be produced if the SDK's session store no longer holds the id. A rule that merely dropped
            // ── our tracker row would answer this request normally and fail here.
            var afterReplace = await host.SendAsync(client, AccountA, first, InstanceX, Ping(id: 3));
            afterReplace.Status.ShouldBe(HttpStatusCode.NotFound,
                $"the replaced session must be gone from the SDK's session store, not merely from our " +
                $"tracker; got {(int)afterReplace.Status} with body: {afterReplace.Body}");

            // And the SDK says WHY, which distinguishes "session evicted" from "route not found" — a 404
            // for the wrong reason would otherwise satisfy the line above.
            afterReplace.Body.ShouldContain("-32001", Case.Sensitive,
                "the 404 must be the MCP SDK's 'Session not found', not a routing 404");

            // The replacement is live.
            var newSessionWorks = await host.SendAsync(client, AccountA, second, InstanceX, Ping(id: 4));
            newSessionWorks.Status.ShouldBe(HttpStatusCode.OK, "the replacement session must be usable");

            // Our own bookkeeping settled too: one identity, one session — not two.
            tracker.ActiveSessionCount.ShouldBe(1,
                "the displaced session's tracker row must be removed, leaving exactly one live session");
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe(second);
            reaper.TrackedIdentityCount.ShouldBe(1);
        }

        // ───────────────────────────── cross-account isolation (DoD 2) ─────────────────────────────

        /// <summary>
        /// Two DIFFERENT accounts presenting the SAME instance id do not affect each other's sessions.
        /// The account comes from the validated bearer credential, so a spoofed instance id can only ever
        /// reach the spoofer's own sessions.
        /// </summary>
        [Fact]
        public async Task TwoDifferentAccounts_SendingTheSameInstanceId_DoNotAffectEachOther()
        {
            await using var host = await StartOAuthHostAsync();
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            using var client = NewClient();

            var sessionA = await host.InitializeAsync(client, AccountA, InstanceX);
            var sessionB = await host.InitializeAsync(client, AccountB, InstanceX);

            sessionA.ShouldNotBeNullOrEmpty();
            sessionB.ShouldNotBeNullOrEmpty();
            sessionB.ShouldNotBe(sessionA);

            // Neither displaced the other: BOTH are still served.
            (await host.SendAsync(client, AccountA, sessionA, InstanceX, Ping(id: 2))).Status
                .ShouldBe(HttpStatusCode.OK, "account A's session must survive account B connecting with the same instance id");
            (await host.SendAsync(client, AccountB, sessionB, InstanceX, Ping(id: 3))).Status
                .ShouldBe(HttpStatusCode.OK);

            tracker.ActiveSessionCount.ShouldBe(2, "two accounts hold two independent sessions");

            // Now make account B replace ITS OWN session and confirm the blast radius stops at B.
            var sessionB2 = await host.InitializeAsync(client, AccountB, InstanceX);
            (await host.SendAsync(client, AccountB, sessionB, InstanceX, Ping(id: 4))).Status
                .ShouldBe(HttpStatusCode.NotFound, "B's own earlier session is the one that gets replaced");
            (await host.SendAsync(client, AccountA, sessionA, InstanceX, Ping(id: 5))).Status
                .ShouldBe(HttpStatusCode.OK, "A's session must be untouched by B's replace");
            (await host.SendAsync(client, AccountB, sessionB2, InstanceX, Ping(id: 6))).Status
                .ShouldBe(HttpStatusCode.OK);
        }

        /// <summary>
        /// ONE account with TWO different installations — the everyday desktop-plus-laptop case — stacks.
        /// Both sessions stay live, and each still replaces only its own predecessor.
        ///
        /// <para>This is the most user-visible way the rule could go wrong: a key that ignored the instance
        /// id would make signing in on a laptop silently kill the session on the desktop, which reads as a
        /// network fault to the user. Covered at the unit level too, but asserted here because this is the
        /// path production actually takes.</para>
        /// </summary>
        [Fact]
        public async Task OneAccountWithTwoInstallations_StacksAndEachReplacesOnlyItsOwn()
        {
            await using var host = await StartOAuthHostAsync();
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            using var client = NewClient();

            var desktop = await host.InitializeAsync(client, AccountA, InstanceX);
            var laptop = await host.InitializeAsync(client, AccountA, InstanceY);

            laptop.ShouldNotBe(desktop);
            tracker.ActiveSessionCount.ShouldBe(2, "two installations of one account are two sessions");

            // Neither displaced the other.
            (await host.SendAsync(client, AccountA, desktop, InstanceX, Ping(id: 2))).Status
                .ShouldBe(HttpStatusCode.OK, "the desktop session must survive the laptop connecting");
            (await host.SendAsync(client, AccountA, laptop, InstanceY, Ping(id: 3))).Status
                .ShouldBe(HttpStatusCode.OK);

            // The laptop reconnecting replaces ONLY the laptop's session.
            var laptop2 = await host.InitializeAsync(client, AccountA, InstanceY);
            (await host.SendAsync(client, AccountA, laptop, InstanceY, Ping(id: 4))).Status
                .ShouldBe(HttpStatusCode.NotFound, "the laptop's own earlier session is replaced");
            (await host.SendAsync(client, AccountA, desktop, InstanceX, Ping(id: 5))).Status
                .ShouldBe(HttpStatusCode.OK, "the desktop must be untouched by the laptop's reconnect");
            (await host.SendAsync(client, AccountA, laptop2, InstanceY, Ping(id: 6))).Status
                .ShouldBe(HttpStatusCode.OK);

            tracker.ActiveSessionCount.ShouldBe(2);
        }

        // ───────────────────────────── the compatibility path (DoD 3) ─────────────────────────────

        /// <summary>
        /// A client that sends NO instance id behaves exactly as it does today: its session is tracked, it
        /// is not replaced, it is not rejected, and nothing warns.
        ///
        /// <para><b>This is the property that makes the container deployable with every released client
        /// unchanged</b> — no synchronised app release. Read together with
        /// <see cref="SecondConnection_SameAccountAndInstance_ReplacesTheFirst_AndTheSdkReleasesIt"/>: on
        /// its own, this test would be satisfied by a rule that never replaces anything.</para>
        /// </summary>
        [Fact]
        public async Task NoInstanceIdHeader_SessionsStackExactlyAsToday_AndNothingWarns()
        {
            await using var host = await StartOAuthHostAsync(captureLogs: true);
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            var reaper = host.Services.GetRequiredService<IMcpSessionReaper>();
            using var client = NewClient();

            // Same account, three connections, NO instance id on any of them.
            var s1 = await host.InitializeAsync(client, AccountA, instanceId: null);
            var s2 = await host.InitializeAsync(client, AccountA, instanceId: null);
            var s3 = await host.InitializeAsync(client, AccountA, instanceId: null);

            new[] { s1, s2, s3 }.Distinct().Count().ShouldBe(3, "three distinct sessions");

            // Not replaced: all three are still served. This is today's stacking behaviour, unchanged.
            foreach (var session in new[] { s1, s2, s3 })
            {
                var response = await host.SendAsync(client, AccountA, session, instanceId: null, payload: Ping(id: 9));
                response.Status.ShouldBe(HttpStatusCode.OK,
                    $"a header-less client must not be replaced or rejected; session {session} got " +
                    $"{(int)response.Status} with body: {response.Body}");
            }

            tracker.ActiveSessionCount.ShouldBe(3, "header-less sessions stack, exactly as before this change");
            reaper.TrackedIdentityCount.ShouldBe(0,
                "no identity was presented, so the reaper must hold nothing at all — not an empty-string slot");

            // ── No error or warning storm FROM THIS CHANGE. ────────────────────────────────────────────
            // Scoped by CATEGORY on purpose. An unscoped "no warnings at all" assertion is what this test
            // originally had, and it failed on pre-existing `tools/list` retry warnings that have nothing to
            // do with the header-less path — asserting a property of the whole test environment rather than
            // of the change. Scoping keeps it falsifiable: any warning or error raised by the reaper or the
            // evictor on a path where no identity was ever presented is a genuine defect.
            var fromThisChange = host.WarningsAndErrorsFrom(
                nameof(McpSessionReaper), nameof(McpSdkSessionEvictor),
                nameof(Server.Transport.StreamableHttpTransportLayer));
            fromThisChange.ShouldBeEmpty(
                "the header-less path must produce no warning or error from the replace-by-identity code; got: " +
                string.Join(" | ", fromThisChange));

            // ── The guard that keeps the assertion above from being vacuous. ───────────────────────────
            // A sink that captures nothing satisfies "empty" while the code logs a storm, so prove the sink
            // is alive AND that it observes the Warning level specifically. The probe is emitted through the
            // host's own ILoggerFactory under a sentinel category, so it exercises the real pipeline without
            // polluting the category-scoped assertion above.
            host.AllLogText.ShouldNotBeNullOrEmpty("the log sink captured nothing at all — it is not wired up");
            const string sentinel = "replace-by-identity-sink-liveness-probe";
            host.EmitProbeWarning(sentinel);
            string.Join(" | ", host.WarningsAndErrors).ShouldContain(sentinel, Case.Sensitive,
                "the sink must be able to observe Warning-level records, or the emptiness assertion above proves nothing");
        }

        // ───────────────────────────── opaque-token validation over the wire (DoD 7) ─────────────────

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef", "over-length")]
        [InlineData("0123456789abcdef0123456789abcde", "under-length")]
        [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", "non-hex, valid length")]
        [InlineData("../../etc/passwd0123456789abcdef", "path traversal, valid length")]
        [InlineData("' OR 1=1 --0123456789abcdef01234", "quote/comment payload, valid length")]
        public async Task MalformedInstanceId_IsRejectedWith400_AndNoSessionIsEstablished(string malformed, string why)
        {
            // Log capture is on because the REJECTION path is the one carrying arbitrary client input, and
            // therefore the one where an unescaped interpolation would actually matter. The replace path is
            // covered separately; without this, "nothing derived from the value reaches a log line" would be
            // asserted only for values that already passed validation.
            await using var host = await StartOAuthHostAsync(captureLogs: true, captureAllLevels: true);
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            var reaper = host.Services.GetRequiredService<IMcpSessionReaper>();
            using var client = NewClient();

            var response = await host.SendRawAsync(client, AccountA, sessionId: null, instanceId: malformed,
                payload: InitializePayload());

            response.Status.ShouldBe(HttpStatusCode.BadRequest,
                $"a present-but-malformed instance id ({why}) must be rejected, never silently ignored; " +
                $"got {(int)response.Status} with body: {response.Body}");
            response.Body.ShouldContain(InstanceIdentityHeader.InvalidInstanceIdError, Case.Sensitive);

            // Terminal: the request never reached a handler, so no session exists and no slot was claimed.
            response.SessionId.ShouldBeNullOrEmpty("a rejected request must not mint a session id");
            tracker.ActiveSessionCount.ShouldBe(0, "no MCP session may be established for a rejected request");
            reaper.TrackedIdentityCount.ShouldBe(0);

            // The offending value is NOT echoed back — reflecting an unvalidated client string into a
            // response body is how an injection payload gets a second delivery channel.
            response.Body.ShouldNotContain(malformed, Case.Sensitive,
                "the rejected value must not be echoed into the response body");

            // …and it does not reach a log line either. This is the half that matters most: the value is
            // arbitrary client input, so an unescaped interpolation here is a log-injection primitive.
            host.AllLogText.ShouldNotContain(malformed, Case.Sensitive,
                "the rejected instance id must not be written to any log record");

            // Guard against vacuity: the sink must actually have observed this request's processing. A dead
            // sink would satisfy the assertion above with the value being logged in full.
            host.AllLogText.ShouldNotBeNullOrEmpty("the log sink captured nothing at all — it is not wired up");
        }

        /// <summary>
        /// A CR/LF-bearing value is rejected. Kestrel refuses illegal header bytes at the protocol level, so
        /// this asserts the OUTCOME — the request does not produce a working session — rather than a
        /// specific status code, and the charset gate itself is pinned exactly in
        /// <c>InstanceIdentityHeaderTests</c> where the value can be handed in directly.
        /// </summary>
        [Fact]
        public async Task InstanceIdContainingCrLf_NeverProducesAWorkingSession()
        {
            await using var host = await StartOAuthHostAsync();
            var tracker = host.Services.GetRequiredService<IMcpSessionTracker>();
            using var client = NewClient();

            // Exactly the valid LENGTH, so only the charset can reject it.
            const string crlf = "0123456789abcdef\r\n6789abcdef0123";
            crlf.Length.ShouldBe(InstanceIdentityHeader.ExpectedLength);

            HttpStatusCode? status = null;
            try
            {
                var response = await host.SendRawAsync(client, AccountA, sessionId: null, instanceId: crlf,
                    payload: InitializePayload());
                status = response.Status;
            }
            catch (HttpRequestException)
            {
                // Also acceptable: the client/server stack refused to transmit the illegal header at all.
            }

            if (status.HasValue)
            {
                ((int)status.Value).ShouldBeGreaterThanOrEqualTo(400,
                    $"a CR/LF-bearing instance id must never be accepted; got {(int)status.Value}");
            }

            tracker.ActiveSessionCount.ShouldBe(0, "no session may be established for a CR/LF-bearing value");
        }

        // ───────────────────────────── log hygiene (DoD 8) ─────────────────────────────

        /// <summary>
        /// The instance id is never written to a log line — not on the replace path, not beside the bearer
        /// credential, not anywhere. Logs carry only a truncated, non-reversible fingerprint.
        /// </summary>
        [Fact]
        public async Task Replace_NeverWritesTheInstanceIdOrTheBearerTokenToALogLine()
        {
            await using var host = await StartOAuthHostAsync(captureLogs: true, captureAllLevels: true);
            using var client = NewClient();

            await host.InitializeAsync(client, AccountA, InstanceX);
            var second = await host.InitializeAsync(client, AccountA, InstanceX);
            second.ShouldNotBeNullOrEmpty();

            var log = host.AllLogText;

            // The replace really happened — otherwise this test would pass by logging nothing at all, which
            // is the vacuous way to satisfy "the value never appears".
            log.ShouldContain("MCP session replaced by identity", Case.Sensitive,
                "the replace path must have run for this assertion to mean anything");

            log.ShouldNotContain(InstanceX, Case.Insensitive,
                "the raw instance id must never reach a log line");
            log.ShouldNotContain(host.TokenFor(AccountA), Case.Sensitive,
                "the bearer credential must never reach a log line");

            // What IS logged is the fingerprint, and it is genuinely present — so the operator-facing
            // correlation handle exists rather than the id having simply been dropped.
            log.ShouldContain(InstanceIdentityHeader.Fingerprint(InstanceX), Case.Sensitive,
                "the log must carry the non-reversible fingerprint so two installations can be told apart");
        }

        // ───────────────────────────── harness ─────────────────────────────

        static HttpClient NewClient() => new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        static object InitializePayload() => new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "replace-by-identity-test", version = "1.0.0" }
            }
        };

        /// <summary>
        /// The liveness probe. <c>ping</c> is deliberate: it is handled entirely inside the MCP session, so
        /// it answers 200 for a live session and the SDK's <c>404 / -32001</c> for an evicted one, which is
        /// exactly the discrimination these tests need.
        ///
        /// <para><c>tools/list</c> was the obvious choice and is the wrong one here: with no engine plugin
        /// attached it retries ten times and emits ten pre-existing WARNINGs plus an ERROR per call —
        /// noise unrelated to this change that took 10 s per probe and swamped the log-hygiene assertions.</para>
        /// </summary>
        static object Ping(int id) => new { jsonrpc = "2.0", id, method = "ping" };

        static async Task<OAuthHost> StartOAuthHostAsync(bool captureLogs = false, bool captureAllLevels = false)
        {
            var port = FreeLoopbackPort();
            const string issuer = "https://as.example";              // never fetched: the JWKS provider is replaced below
            var publicUrl = $"http://127.0.0.1:{port}/mcp";

            var key = TestJwt.CreateKey();

            var dataArguments = new DataArguments(new[]
            {
                $"port={port}",
                "client-transport=streamableHttp",
                "auth=oauth",
                $"auth-issuer={issuer}",
                $"public-url={publicUrl}",
            });

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            var sink = new ListLoggerProvider();
            if (captureLogs)
            {
                builder.Logging.AddProvider(sink);
                builder.Logging.SetMinimumLevel(captureAllLevels ? LogLevel.Trace : LogLevel.Debug);
            }

            builder.Services
                .WithMcpServer(dataArguments)
                .WithMcpPluginServer(dataArguments);

            // Hermetic: serve the signing key from memory instead of fetching the AS over HTTP.
            builder.Services.AddSingleton<IJwksKeyProvider>(new FakeJwksKeyProvider().Add(Kid, key));

            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseMcpPluginServer(dataArguments);
            await app.StartAsync();

            return new OAuthHost(app, $"http://127.0.0.1:{port}", issuer, publicUrl, key, sink);
        }

        sealed class OAuthHost : IAsyncDisposable
        {
            readonly WebApplication _app;
            readonly ECDsa _key;
            readonly string _baseUrl;
            readonly string _issuer;
            readonly string _audience;
            readonly ListLoggerProvider _sink;
            readonly ConcurrentDictionary<string, string> _tokens = new ConcurrentDictionary<string, string>();

            public OAuthHost(WebApplication app, string baseUrl, string issuer, string audience, ECDsa key, ListLoggerProvider sink)
            {
                _app = app;
                _baseUrl = baseUrl;
                _issuer = issuer;
                _audience = audience;
                _key = key;
                _sink = sink;
            }

            public IServiceProvider Services => _app.Services;

            /// <summary>A distinct agent token per account, so `sub` is the only thing that differs.</summary>
            public string TokenFor(string accountSub) => _tokens.GetOrAdd(accountSub, sub =>
                TestJwt.SignEs256(_key, Kid, TestJwt.Claims(
                    _issuer, _audience, DateTimeOffset.UtcNow.AddHours(1),
                    sub: sub, scope: ConnectionIdentity.ScopeAgent)));

            public IReadOnlyList<string> WarningsAndErrors => _sink.AtOrAbove(LogLevel.Warning);

            /// <summary>
            /// Warning-or-worse records whose logger CATEGORY mentions one of <paramref name="categoryFragments"/>.
            /// Scoping by category is what makes a "nothing warned" assertion about THIS change rather than
            /// about the whole test environment.
            /// </summary>
            public IReadOnlyList<string> WarningsAndErrorsFrom(params string[] categoryFragments)
                => _sink.AtOrAbove(LogLevel.Warning, categoryFragments);

            public string AllLogText => _sink.AllText;

            /// <summary>
            /// Emits a Warning through the host's REAL logger factory, so a test can prove the sink observes
            /// that level before relying on the sink being empty.
            /// </summary>
            public void EmitProbeWarning(string message)
            {
                var factory = _app.Services.GetRequiredService<ILoggerFactory>();
                factory.CreateLogger("SinkLivenessProbe").LogWarning("{probe}", message);
            }

            /// <summary>Runs a full initialize + initialized handshake and returns the minted session id.</summary>
            public async Task<string> InitializeAsync(HttpClient client, string accountSub, string? instanceId)
            {
                var response = await SendRawAsync(client, accountSub, sessionId: null, instanceId: instanceId,
                    payload: InitializePayload());

                response.Status.ShouldBe(HttpStatusCode.OK,
                    $"initialize must succeed; got {(int)response.Status} with body: {response.Body}");
                response.SessionId.ShouldNotBeNullOrEmpty("initialize must mint an Mcp-Session-Id");

                var initialized = await SendRawAsync(client, accountSub, response.SessionId, instanceId,
                    new { jsonrpc = "2.0", method = "notifications/initialized" });
                initialized.Status.ShouldBe(HttpStatusCode.Accepted,
                    $"notifications/initialized must be accepted; got {(int)initialized.Status}: {initialized.Body}");

                return response.SessionId!;
            }

            /// <summary>Sends a request and asserts nothing about the outcome — the caller does that.</summary>
            public Task<Response> SendAsync(HttpClient client, string accountSub, string? sessionId, string? instanceId, object payload)
                => SendRawAsync(client, accountSub, sessionId, instanceId, payload);

            public async Task<Response> SendRawAsync(HttpClient client, string accountSub, string? sessionId, string? instanceId, object payload)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(accountSub));
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                if (!string.IsNullOrEmpty(sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                if (instanceId != null)
                    request.Headers.TryAddWithoutValidation(InstanceIdentityHeader.HeaderName, instanceId);
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var issued = response.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null;
                return new Response(response.StatusCode, Unwrap(body), issued);
            }

            public async ValueTask DisposeAsync()
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
                _key.Dispose();
            }
        }

        sealed class Response
        {
            public Response(HttpStatusCode status, string body, string? sessionId)
            {
                Status = status;
                Body = body;
                SessionId = sessionId;
            }

            public HttpStatusCode Status { get; }
            public string Body { get; }
            public string? SessionId { get; }
        }

        /// <summary>
        /// Minimal in-memory Microsoft.Extensions.Logging sink. The host clears every provider, so without
        /// this the reaper's and evictor's records are invisible — and a log-hygiene assertion wired to a
        /// sink that sees nothing would pass with the value being logged in full.
        /// </summary>
        sealed class ListLoggerProvider : ILoggerProvider
        {
            readonly ConcurrentQueue<(LogLevel Level, string Category, string Message)> _records =
                new ConcurrentQueue<(LogLevel, string, string)>();

            public ILogger CreateLogger(string categoryName) => new ListLogger(_records, categoryName);

            public IReadOnlyList<string> AtOrAbove(LogLevel level)
                => _records.Where(r => r.Level >= level).Select(Format).ToArray();

            /// <summary>
            /// Warning-or-worse records from categories matching any fragment. An EMPTY
            /// <paramref name="categoryFragments"/> matches nothing rather than everything — the opposite
            /// default would silently turn a scoped assertion back into an unscoped one if the argument list
            /// were ever dropped.
            /// </summary>
            public IReadOnlyList<string> AtOrAbove(LogLevel level, string[] categoryFragments)
                => _records
                    .Where(r => r.Level >= level)
                    .Where(r => categoryFragments.Any(f => r.Category.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(Format)
                    .ToArray();

            public string AllText => string.Join(Environment.NewLine, _records.Select(Format));

            static string Format((LogLevel Level, string Category, string Message) r)
                => $"[{r.Level}] {r.Category}: {r.Message}";

            public void Dispose() { }

            sealed class ListLogger : ILogger
            {
                readonly ConcurrentQueue<(LogLevel, string, string)> _records;
                readonly string _category;

                public ListLogger(ConcurrentQueue<(LogLevel, string, string)> records, string category)
                {
                    _records = records;
                    _category = category;
                }

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    var message = formatter(state, exception);
                    if (exception != null)
                        message += " || " + exception;
                    _records.Enqueue((logLevel, _category, message));
                }
            }
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
