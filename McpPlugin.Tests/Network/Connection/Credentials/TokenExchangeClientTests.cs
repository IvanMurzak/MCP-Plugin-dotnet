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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// Wire-contract coverage for <see cref="TokenExchangeClient"/> against the a5-frozen RFC 8693
    /// exchange shape (unified-machine-auth 04 §4; server merged on AGS dev, PR #592). The shape is
    /// pinned parameter-by-parameter because the server validates it strictly (`invalid_scope` for
    /// any scope other than <c>mcp:plugin</c>; <c>audience</c>/<c>resource</c> accepted only as the
    /// exact <c>urn:agd:hub</c> — omitting both is the contract-safe choice this client makes).
    /// </summary>
    public sealed class TokenExchangeClientTests
    {
        const string OwnClientId = "unity-mcp-plugin";
        const string AgentAccessToken = "eyJ.AGENT-ES256.sig";
        const string ServerTarget = "https://ai-game.dev";

        static readonly string SuccessJson =
            "{\"access_token\":\"eyJ.PLUGIN.sig\",\"issued_token_type\":\"urn:ietf:params:oauth:token-type:access_token\"," +
            "\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"RT-plugin-new\",\"scope\":\"mcp:plugin\",\"sub\":\"usr_123\"}";

        static TokenExchangeClient NewClient(RecordingHttpMessageHandler handler, Func<DateTimeOffset>? clock = null)
            => new TokenExchangeClient(ServerTarget, OwnClientId, new HttpClient(handler), clock, timeoutMs: null);

        [Fact]
        public async Task Exchange_SendsExactlyTheFrozenA5WireShape()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var client = NewClient(handler);

            var result = await client.ExchangeAsync(AgentAccessToken, ServerTarget);

            result.Succeeded.ShouldBeTrue();
            var request = handler.Requests.ShouldHaveSingleItem();
            request.Url.ShouldBe("https://ai-game.dev/oauth/token");
            var form = request.Form;
            form["grant_type"].ShouldBe("urn:ietf:params:oauth:grant-type:token-exchange");
            form["subject_token"].ShouldBe(AgentAccessToken);
            form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:access_token"); // the exact URN
            form["client_id"].ShouldBe(OwnClientId);
            form["scope"].ShouldBe("mcp:plugin");
            // audience/resource: only the exact "urn:agd:hub" would be accepted — this client omits
            // both rather than relying on the server's current trailing-slash leniency.
            form.ContainsKey("audience").ShouldBeFalse();
            form.ContainsKey("resource").ShouldBeFalse();
            form.Keys.ShouldBe(new[] { "grant_type", "subject_token", "subject_token_type", "client_id", "scope" }, ignoreOrder: true);
        }

        [Fact]
        public async Task Exchange_Success_CarriesTheFullA5ResponseContract()
        {
            var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var client = NewClient(handler, clock: () => now);

            var result = await client.ExchangeAsync(AgentAccessToken, ServerTarget);

            result.Succeeded.ShouldBeTrue();
            result.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
            result.RefreshToken.ShouldBe("RT-plugin-new"); // RFC 8693 §2.2.1 documented exception
            result.ExpiresAt.ShouldBe(now.AddSeconds(3600));
            result.Scope.ShouldBe("mcp:plugin");
            result.Subject.ShouldBe("usr_123"); // O5 — no path writes a subject-less credential
            result.IssuedTokenType.ShouldBe("urn:ietf:params:oauth:token-type:access_token");
        }

        [Fact]
        public async Task Exchange_OAuthError_FailsClosedWithTheErrorText()
        {
            var handler = new RecordingHttpMessageHandler()
                .RespondWith(HttpStatusCode.BadRequest, "{\"error\":\"invalid_scope\",\"error_description\":\"only mcp:plugin\"}");
            var client = NewClient(handler);

            var result = await client.ExchangeAsync(AgentAccessToken, ServerTarget);

            result.Succeeded.ShouldBeFalse();
            result.AccessToken.ShouldBeNull();
            result.FailureReason.ShouldContain("invalid_scope");
        }

        [Fact]
        public async Task Exchange_NoSubjectToken_FailsClosedWithoutANetworkCall()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, SuccessJson);
            var client = NewClient(handler);

            var result = await client.ExchangeAsync("", ServerTarget);

            result.Succeeded.ShouldBeFalse();
            handler.Requests.ShouldBeEmpty();
        }

        [Fact]
        public void Construction_EnforcesTheA5ClientIdLengthCap()
        {
            var tooLong = new string('a', TokenExchangeClient.MaxClientIdLength + 1);
            Should.Throw<ArgumentException>(() => new TokenExchangeClient(ServerTarget, tooLong));
            // And the cap itself is the frozen value.
            TokenExchangeClient.MaxClientIdLength.ShouldBe(64);
        }

        [Fact]
        public void ClientId_IsExposed_SoTheCommitHelperStampsTheFamilyWithThePresentedId()
        {
            var client = new TokenExchangeClient(ServerTarget, OwnClientId);
            client.ClientId.ShouldBe(OwnClientId); // design D8: stamp what was actually presented
        }
    }
}
