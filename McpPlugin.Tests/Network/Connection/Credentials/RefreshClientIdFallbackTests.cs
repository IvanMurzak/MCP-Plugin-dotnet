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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// The client-id mismatch classification and the fallback order (04 §3 rule 7) — both pinned
    /// to AI-Game-Dev-CLI-Core's <c>isClientIdMismatchText</c>, <c>FIRST_PARTY_REFRESH_CLIENT_IDS</c>
    /// and <c>orderFallbackClientIds</c>.
    /// </summary>
    public sealed class RefreshClientIdFallbackTests
    {
        const string ServerTarget = "https://ai-game.dev";
        const string MismatchJson = "{\"error\":\"invalid_grant\",\"error_description\":\"client_id does not match the refresh token\"}";

        static readonly Func<DateTimeOffset> Clock = () => new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void DefaultList_MatchesCliCore_InOrder()
        {
            RefreshClientIdFallback.DefaultFirstPartyClientIds.ShouldBe(new[]
            {
                "unity-mcp-plugin", "unity-mcp-cli",
                "unreal-mcp-plugin", "unreal-mcp-cli",
                "godot-mcp-plugin", "godot-cli",
                "brutal-cli",
                "agd_enroll",
                "ai-game-dev-app",
            });
        }

        [Theory]
        [InlineData("unity-mcp-plugin", "unity-mcp-cli,agd_enroll,unreal-mcp-plugin,unreal-mcp-cli,godot-mcp-plugin,godot-cli,brutal-cli,ai-game-dev-app")]
        [InlineData("godot-mcp-plugin", "godot-cli,agd_enroll,unity-mcp-plugin,unity-mcp-cli,unreal-mcp-plugin,unreal-mcp-cli,brutal-cli,ai-game-dev-app")]
        [InlineData("unity-mcp-cli", "unity-mcp-plugin,agd_enroll,unreal-mcp-plugin,unreal-mcp-cli,godot-mcp-plugin,godot-cli,brutal-cli,ai-game-dev-app")]
        [InlineData("brutal-mcp-plugin", "brutal-cli,agd_enroll,unity-mcp-plugin,unity-mcp-cli,unreal-mcp-plugin,unreal-mcp-cli,godot-mcp-plugin,godot-cli,ai-game-dev-app")]
        [InlineData(null, "agd_enroll,unity-mcp-plugin,unity-mcp-cli,unreal-mcp-plugin,unreal-mcp-cli,godot-mcp-plugin,godot-cli,brutal-cli,ai-game-dev-app")]
        public void Order_SiblingThenEnrollThenTheRest_NeverTheDefault(string? defaultClientId, string expected)
        {
            RefreshClientIdFallback.Order(RefreshClientIdFallback.DefaultFirstPartyClientIds, defaultClientId)
                .ShouldBe(expected.Split(','));
        }

        [Fact]
        public void Order_OnlyIdsFromTheList_Deduplicated_NoBlanks()
        {
            RefreshClientIdFallback.Order(new[] { "x", " ", "agd_enroll", "x", "unity-mcp-plugin" }, "unity-mcp-plugin")
                .ShouldBe(new[] { "agd_enroll", "x" }); // the sibling (unity-mcp-cli) is not in the list
            RefreshClientIdFallback.Order(Array.Empty<string>(), "unity-mcp-plugin").ShouldBeEmpty();
        }

        [Theory]
        [InlineData("client_id does not match the refresh token", true)]
        [InlineData("invalid_grant: client_id does not match the refresh token", true)]
        [InlineData("CLIENT_ID DOES NOT MATCH", true)]
        [InlineData("refresh token has expired", false)]
        [InlineData("client id mismatch", false)]
        [InlineData(null, false)]
        public void MismatchText_SameTestAsCliCore(string? text, bool expected)
            => RefreshClientIdFallback.IsClientIdMismatchText(text).ShouldBe(expected);

        [Fact]
        public void Parse_InvalidGrantNamingTheMismatch_IsClientIdMismatch_NotInvalidGrant()
        {
            var result = HttpTokenRefresher.ParseTokenResponse(false, 400, MismatchJson, Clock);

            result.FailureKind.ShouldBe(TokenRefreshFailureKind.ClientIdMismatch);
            result.FailureReason.ShouldBe("invalid_grant: client_id does not match the refresh token");

            // Positive control: any other invalid_grant stays the terminal kind.
            HttpTokenRefresher.ParseTokenResponse(false, 400, "{\"error\":\"invalid_grant\",\"error_description\":\"refresh token has expired\"}", Clock)
                .FailureKind.ShouldBe(TokenRefreshFailureKind.InvalidGrant);
        }

        [Fact]
        public async Task RateDiscipline_IsPerClientId_SoTheFallbackReachesTheServerUnderEachId()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.BadRequest, MismatchJson);
            var refresher = new HttpTokenRefresher(ServerTarget, "unity-mcp-plugin", new HttpClient(handler), Clock, attemptWindow: null, timeoutMs: null);

            (await refresher.RefreshAsync(new TokenRefreshRequest("RT-a", ServerTarget, clientId: null))).FailureKind.ShouldBe(TokenRefreshFailureKind.ClientIdMismatch);
            (await refresher.RefreshAsync(new TokenRefreshRequest("RT-a", ServerTarget, "unity-mcp-cli"))).FailureKind.ShouldBe(TokenRefreshFailureKind.ClientIdMismatch);
            // The default presented explicitly shares the default's attempt; a repeat id is replayed.
            await refresher.RefreshAsync(new TokenRefreshRequest("RT-a", ServerTarget, "unity-mcp-plugin"));
            await refresher.RefreshAsync(new TokenRefreshRequest("RT-a", ServerTarget, "unity-mcp-cli"));

            handler.Requests.Select(r => r.Form["client_id"]).ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli" });
        }
    }
}
