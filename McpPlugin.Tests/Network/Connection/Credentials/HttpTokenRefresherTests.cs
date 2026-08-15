/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// Wire-contract coverage for the shared <see cref="HttpTokenRefresher"/> (unified-machine-auth
    /// 04 §3, task b3). The two security-critical rules each have a mandated plant (G-SEC-1):
    /// <list type="bullet">
    ///   <item><b>Stored-clientId rule (04 §3.2, probe Q1):</b> a refresher that ignores the
    ///   family's stored <c>clientId</c> and presents its component default cross-mints the family
    ///   → <see cref="Refresh_PresentsTheFamilysStoredClientId_NeverTheComponentDefault"/> must go
    ///   RED under that mutation.</item>
    ///   <item><b>Omit-scope rule (04 §3.3, P0-3):</b> a refresher that sends a default
    ///   <c>scope</c> permanently narrows an agent family →
    ///   <see cref="Refresh_OmitsScopeAndResourceEntirely"/> must go RED under that mutation.</item>
    /// </list>
    /// </summary>
    public sealed class HttpTokenRefresherTests
    {
        const string ComponentClientId = "unity-mcp-plugin";
        const string StoredClientId = "app-dcr-4f2a9c31"; // ≠ the component default, by construction
        const string RefreshToken = "RT-family-aaa";
        const string ServerTarget = "https://ai-game.dev";

        static readonly string SuccessJson =
            "{\"access_token\":\"eyJ.NEW.xyz\",\"refresh_token\":\"RT-family-bbb\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";

        static HttpTokenRefresher NewRefresher(RecordingHttpMessageHandler handler,
            Func<DateTimeOffset>? clock = null, TimeSpan? window = null, int? timeoutMs = null)
            => new HttpTokenRefresher(ServerTarget, ComponentClientId, new HttpClient(handler), clock, window, timeoutMs);

        // ── The stored-clientId rule (04 §3.2) — plant target 1, mirrors probe Q1. ──

        [Fact]
        public async Task Refresh_PresentsTheFamilysStoredClientId_NeverTheComponentDefault()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.Succeeded.ShouldBeTrue();
            var form = handler.Requests.ShouldHaveSingleItem().Form;
            // The cross-mint discriminator: the wire request carries the STORED id, and provably
            // not the component default (which is a different string by construction).
            form["client_id"].ShouldBe(StoredClientId);
            form["client_id"].ShouldNotBe(ComponentClientId);
        }

        [Fact]
        public async Task Refresh_LegacyFamilyOfUnknownId_PresentsTheComponentDefault()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            // 04 §3.7: ClientId null = a v1-adopted legacy family — status-quo component default.
            await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, clientId: null));

            handler.Requests.ShouldHaveSingleItem().Form["client_id"].ShouldBe(ComponentClientId);
        }

        [Fact]
        public async Task Refresh_LegacyTwoStringApi_PresentsTheComponentDefault_AndStillOmitsScope()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            ITokenRefresher refresher = NewRefresher(handler);

            await refresher.RefreshAsync(RefreshToken, ServerTarget);

            var form = handler.Requests.ShouldHaveSingleItem().Form;
            form["client_id"].ShouldBe(ComponentClientId);
            form.ContainsKey("scope").ShouldBeFalse(); // unlike the pre-b3 engine refreshers
        }

        // ── The omit-scope/omit-resource rule (04 §3.3, P0-3) — plant target 2. ──

        [Fact]
        public async Task Refresh_OmitsScopeAndResourceEntirely()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            // An AGENT-plane family (a stored id minted for mcp:agent): sending any component
            // default scope here would permanently narrow it (P0-3).
            await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            var form = handler.Requests.ShouldHaveSingleItem().Form;
            form.ContainsKey("scope").ShouldBeFalse();
            form.ContainsKey("resource").ShouldBeFalse();
            form.ContainsKey("audience").ShouldBeFalse();
            // Belt-and-braces: the request is EXACTLY the three contract parameters — a mutation
            // that adds any fourth parameter (scope included) turns this red.
            form.Keys.ShouldBe(new[] { "grant_type", "refresh_token", "client_id" }, ignoreOrder: true);
            form["grant_type"].ShouldBe("refresh_token");
            form["refresh_token"].ShouldBe(RefreshToken);
        }

        [Fact]
        public async Task Refresh_PostsToTheOAuthTokenEndpoint_OnTheNormalizedAsRoot()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            // A stored hub target ("…/mcp", trailing slash) must normalize to the AS root.
            await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, "https://ai-game.dev/mcp/", StoredClientId));

            handler.Requests.ShouldHaveSingleItem().Url.ShouldBe("https://ai-game.dev/oauth/token");
        }

        // ── Rotation response without refresh_token keeps the previous one (04 §3.4). ──

        [Fact]
        public async Task Refresh_ResponseWithoutRefreshToken_YieldsNullRefreshToken_SoCallerKeepsPrevious()
        {
            var handler = new RecordingHttpMessageHandler()
                .RespondWith(HttpStatusCode.OK, "{\"access_token\":\"eyJ.NEW.xyz\",\"expires_in\":3600}");
            var refresher = NewRefresher(handler);

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.Succeeded.ShouldBeTrue();
            result.AccessToken.ShouldBe("eyJ.NEW.xyz");
            result.RefreshToken.ShouldBeNull(); // the caller's keep-previous contract
        }

        [Fact]
        public async Task Refresh_Success_ComputesExpiryFromExpiresIn()
        {
            var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler, clock: () => now);

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.ExpiresAt.ShouldBe(now.AddSeconds(3600));
        }

        // ── Failure classification (04 §3.5). ──

        [Fact]
        public async Task Refresh_InvalidGrant_IsClassifiedInvalidGrant()
        {
            var handler = new RecordingHttpMessageHandler()
                .RespondWith(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"revoked\"}");
            var refresher = NewRefresher(handler);

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.Succeeded.ShouldBeFalse();
            result.FailureKind.ShouldBe(TokenRefreshFailureKind.InvalidGrant);
            result.FailureReason.ShouldContain("invalid_grant");
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}")]         // a DIFFERENT OAuth error
        [InlineData(HttpStatusCode.InternalServerError, "{\"detail\":\"boom\"}")]          // 5xx
        [InlineData(HttpStatusCode.OK, "this is not json")]                                // malformed body
        public async Task Refresh_NonInvalidGrantFailures_AreClassifiedTransient(HttpStatusCode status, string body)
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(status, body);
            var refresher = NewRefresher(handler);

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.Succeeded.ShouldBeFalse();
            result.FailureKind.ShouldBe(TokenRefreshFailureKind.Transient);
        }

        // ── The 15 s HTTP timeout (04 §2) — tested at a shrunk test-seam timeout. ──

        [Fact]
        public void HttpTimeout_IsTheSharedContractConstant()
        {
            // The refresher pins the cross-language 04 §2 value — .NET's 100 s default would
            // outlive LOCK_STALE_MS and let a live lock holder be declared stale.
            HttpTokenRefresher.HttpTimeoutMs.ShouldBe(MachineCredentialLock.REFRESH_HTTP_TIMEOUT);
            HttpTokenRefresher.HttpTimeoutMs.ShouldBe(15_000);
        }

        [Fact]
        public async Task Refresh_SlowServer_TimesOutAsTransientFailure()
        {
            var handler = new RecordingHttpMessageHandler()
                .DelayEachResponse(TimeSpan.FromSeconds(30))
                .RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler, timeoutMs: 150); // shrunk seam; production pins 15 s

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            result.Succeeded.ShouldBeFalse();
            result.FailureKind.ShouldBe(TokenRefreshFailureKind.Transient);
            result.FailureReason.ShouldContain("timed out");
        }

        [Fact]
        public async Task Refresh_CallerCancellation_Rethrows_NotAFailureResult()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Should.ThrowAsync<OperationCanceledException>(
                () => refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId), cts.Token));
        }

        // ── Rate discipline (04 §3.6): one network attempt per family per window. ──

        [Fact]
        public async Task Refresh_SameFamilyTwiceInsideTheWindow_MakesOneNetworkAttempt_AndSharesTheResult()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            var first = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));
            var second = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            handler.Requests.Count.ShouldBe(1); // the second call replayed the window's attempt
            second.ShouldBeSameAs(first);
        }

        [Fact]
        public async Task Refresh_DifferentFamilies_AreRateLimitedIndependently()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler);

            await refresher.RefreshAsync(new TokenRefreshRequest("RT-family-agent", ServerTarget, StoredClientId));
            await refresher.RefreshAsync(new TokenRefreshRequest("RT-family-plugin", ServerTarget, StoredClientId));

            handler.Requests.Count.ShouldBe(2); // distinct refresh tokens = distinct families
        }

        [Fact]
        public async Task Refresh_SameFamilyAfterTheWindow_AttemptsAgain()
        {
            var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = NewRefresher(handler, clock: () => now, window: TimeSpan.FromSeconds(60));

            await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));
            now = now.AddSeconds(61); // the window elapsed
            await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, ServerTarget, StoredClientId));

            handler.Requests.Count.ShouldBe(2);
        }

        // ── Fail-closed basics. ──

        [Fact]
        public async Task Refresh_NoServerTargetAnywhere_FailsClosed()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var refresher = new HttpTokenRefresher("", ComponentClientId, new HttpClient(handler));

            var result = await refresher.RefreshAsync(new TokenRefreshRequest(RefreshToken, serverTarget: null, StoredClientId));

            result.Succeeded.ShouldBeFalse();
            handler.Requests.ShouldBeEmpty();
        }

        [Fact]
        public void Construction_RequiresAComponentClientId()
        {
            Should.Throw<ArgumentException>(() => new HttpTokenRefresher(ServerTarget, componentClientId: " "));
        }
    }
}
