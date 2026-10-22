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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// 04 §3 rule 7 (the twin of AI-Game-Dev-CLI-Core PR #36): the authorization server refuses a
    /// refresh presented under the wrong client id with <c>invalid_grant</c> /
    /// "client_id does not match the refresh token" while the token stays LIVE for its mint client.
    /// So a mismatch is never a dead family (no memo, no shared sidecar, no family-dead event), and a
    /// family with NO stored id (a collapsed v1 store, pre-a6 enroll) is retried under the other
    /// first-party ids within the same lock hold, recording the id that works. Runs the real
    /// <see cref="HttpTokenRefresher"/> against <see cref="MintServer"/>, which answers like the AS.
    /// </summary>
    public sealed class PluginCredentialProviderClientIdFallbackTests : IDisposable
    {
        const string ServerTarget = "https://ai-game.dev";
        const string ComponentClientId = "unity-mcp-plugin";
        const string LegacyRefresh = "RT-legacy-aaa";
        const string MismatchReason = "invalid_grant: client_id does not match the refresh token";

        static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        readonly string _baseDir;
        DateTimeOffset _now = Start;

        public PluginCredentialProviderClientIdFallbackTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-clientid-fallback-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        string SidecarPath => Path.Combine(_baseDir, PersistedDeadFamilyMemo.FileName);

        /// <summary>A collapsed v1 store (old Unity plugin 0.84–0.87 rewrite): read as families.legacy, ClientId null.</summary>
        void SeedCollapsedV1(string refreshToken = LegacyRefresh)
        {
            NewStore().WritePlaintextJson(
                "{\"version\":1,\"accessToken\":\"eyJ.V1.sig\",\"refreshToken\":\"" + refreshToken + "\"," +
                "\"expiresAt\":\"2026-01-01T00:00:00+00:00\",\"serverTarget\":\"" + ServerTarget + "\"}");
        }

        void SeedPluginFamily(string clientId, string refreshToken = LegacyRefresh)
        {
            NewStore().Write(new MachineCredentials
            {
                ServerTarget = ServerTarget,
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily
                    {
                        AccessToken = "eyJ.PLUGIN.sig",
                        RefreshToken = refreshToken,
                        ExpiresAt = Start.AddMinutes(30),
                        ClientId = clientId,
                    },
                },
            });
        }

        HttpTokenRefresher NewRefresher(MintServer server)
            => new HttpTokenRefresher(ServerTarget, ComponentClientId, new HttpClient(server.Handler), () => _now, attemptWindow: null, timeoutMs: 2_000);

        PluginCredentialProvider NewProvider(ITokenRefresher refresher, ILogger? logger = null)
            => new PluginCredentialProvider(NewStore(), refresher, logger, clock: () => _now);

        // ── Fallback success: the id that works is recorded on the family. ──

        [Fact]
        public async Task CollapsedV1_MintedByTheSiblingCli_FallsBackToIt_AndRecordsTheClientId()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "unity-mcp-cli");
            using var provider = NewProvider(NewRefresher(server));

            (await provider.RefreshAsync()).ShouldBeTrue();

            // The component default first, then its sibling CLI — inside ONE lock hold.
            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli" });
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);

            var family = NewStore().Read()!.Families!.Legacy!;
            family.ClientId.ShouldBe("unity-mcp-cli"); // persisted: every later refresh presents it directly
            family.RefreshToken.ShouldBe(server.HeadOf("unity-mcp-cli"));
            File.Exists(SidecarPath).ShouldBeFalse();
        }

        [Fact]
        public async Task PreA6Enroll_FamilyWithoutClientId_FallsBackToAgdEnroll_AfterTheSibling()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, RefreshClientIdFallback.EnrollClientId);
            using var provider = NewProvider(NewRefresher(server));

            (await provider.RefreshAsync()).ShouldBeTrue();

            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli", "agd_enroll" });
            NewStore().Read()!.Families!.Legacy!.ClientId.ShouldBe("agd_enroll");
        }

        [Fact]
        public async Task RecordedClientId_IsPresentedDirectly_OnTheNextRefresh()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "godot-cli");
            using (var provider = NewProvider(NewRefresher(server)))
                (await provider.RefreshAsync()).ShouldBeTrue();
            server.PresentedClientIds.Last().ShouldBe("godot-cli");

            var before = server.PresentedClientIds.Count;
            using (var second = NewProvider(NewRefresher(server)))
                (await second.RefreshAsync()).ShouldBeTrue();

            server.PresentedClientIds.Skip(before).ShouldBe(new[] { "godot-cli" }); // no probing any more
        }

        // ── A mismatch is never a dead family. ──

        [Fact]
        public async Task StoredClientId_Mismatch_NoFallback_NotDead_NothingPersisted()
        {
            SeedPluginFamily(clientId: "unity-mcp-plugin");
            var server = new MintServer().Mint(LegacyRefresh, "unity-mcp-cli");
            var logger = new CapturingLogger();
            using var provider = NewProvider(NewRefresher(server), logger);

            (await provider.RefreshAsync()).ShouldBeFalse();

            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin" }); // a stored id is never swapped
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            provider.SignInRequiredReason!.ShouldContain("issued to another application");
            File.Exists(SidecarPath).ShouldBeFalse();
            logger.Warnings.ShouldNotContain(w => w.Contains("Credential family dead"));
            NewStore().Read()!.Families!.Plugin!.ClientId.ShouldBe("unity-mcp-plugin"); // never replaced
        }

        [Fact]
        public async Task EveryIdMismatches_SignInRequired_NoDeadMemo_NoSidecar_ReArmedByANewToken()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "some-third-party-client");
            var logger = new CapturingLogger();
            using var provider = NewProvider(NewRefresher(server), logger);

            (await provider.RefreshAsync()).ShouldBeFalse();

            // The default plus every other first-party id, each exactly once.
            server.PresentedClientIds.ShouldBe(new[]
            {
                "unity-mcp-plugin", "unity-mcp-cli", "agd_enroll",
                "unreal-mcp-plugin", "unreal-mcp-cli", "godot-mcp-plugin", "godot-cli", "brutal-cli", "ai-game-dev-app",
            });
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            provider.SignInRequiredReason.ShouldBe("the saved sign-in was issued to another application (" + MismatchReason + "); sign in again");
            File.Exists(SidecarPath).ShouldBeFalse(); // a CLI may still refresh it — never told it is dead
            logger.Warnings.ShouldNotContain(w => w.Contains("Credential family dead"));

            // No new burst for the same token, even past the refresher's attempt window.
            _now = Start.AddHours(1);
            var afterBurst = server.PresentedClientIds.Count;
            (await provider.RefreshAsync()).ShouldBeFalse();
            server.PresentedClientIds.Count.ShouldBe(afterBurst);

            // Positive control: a different token in the store (a fresh login) re-arms the refresh.
            SeedCollapsedV1("RT-fresh-bbb");
            server.Mint("RT-fresh-bbb", ComponentClientId);
            (await provider.RefreshAsync()).ShouldBeTrue();
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
        }

        [Fact]
        public async Task EmptyFallbackList_DisablesTheFallback()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "unity-mcp-cli");
            using var provider = NewProvider(NewRefresher(server));
            provider.FallbackClientIds = Array.Empty<string>();

            (await provider.RefreshAsync()).ShouldBeFalse();

            server.PresentedClientIds.ShouldBe(new[] { ComponentClientId });
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            File.Exists(SidecarPath).ShouldBeFalse();
        }

        [Fact]
        public async Task HostConfiguredFallbackList_IsOrderedSiblingFirst_AndUsed()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "custom-host-cli");
            using var provider = NewProvider(NewRefresher(server));
            provider.FallbackClientIds = new[] { "custom-host-cli", "unity-mcp-cli" };

            (await provider.RefreshAsync()).ShouldBeTrue();

            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli", "custom-host-cli" });
            NewStore().Read()!.Families!.Legacy!.ClientId.ShouldBe("custom-host-cli");
        }

        [Fact]
        public async Task EngineRefresher_InvalidGrantNamingTheMismatch_IsNotADeadFamily()
        {
            // An engine refresher that predates ClientIdMismatch reports it as plain invalid_grant —
            // and may drop the request's client id, so it gets no fallback probing either.
            SeedCollapsedV1();
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure(MismatchReason, TokenRefreshFailureKind.InvalidGrant));
            var logger = new CapturingLogger();
            using var provider = NewProvider(refresher, logger);

            (await provider.RefreshAsync()).ShouldBeFalse();

            refresher.Requests.ShouldHaveSingleItem().ClientId.ShouldBeNull();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            provider.SignInRequiredReason!.ShouldContain("issued to another application");
            File.Exists(SidecarPath).ShouldBeFalse();
            logger.Warnings.ShouldNotContain(w => w.Contains("Credential family dead"));
        }

        [Fact]
        public async Task SidecarRecordingAMismatch_FromAnOlderPlugin_IsIgnored()
        {
            SeedCollapsedV1();
            // McpPlugin 8.11.x persisted a mismatch as a dead family; that record must not block anyone.
            File.WriteAllText(SidecarPath, PersistedDeadFamilyMemo.Serialize(LegacyRefresh, MismatchReason, Start));
            var server = new MintServer().Mint(LegacyRefresh, "unity-mcp-cli");
            using var provider = NewProvider(NewRefresher(server));

            (await provider.RefreshAsync()).ShouldBeTrue();
            server.PresentedClientIds.ShouldNotBeEmpty();
        }

        // ── Transient + budget (rate discipline from PR #269). ──

        [Fact]
        public async Task TransientDuringFallback_StopsIt_AndTheRetryPresentsTheSameIdFirst()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "agd_enroll");
            server.FailTransientlyOnce("agd_enroll"); // the rotation may have been committed, the answer lost
            using var provider = NewProvider(NewRefresher(server));

            (await provider.RefreshAsync()).ShouldBeFalse();

            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli", "agd_enroll" });
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn); // transient: no sign-in prompt
            File.Exists(SidecarPath).ShouldBeFalse();

            // Past the refresher's 2 s transient cooldown: the retry leads with agd_enroll — the id
            // the server's per-client rotation grace covers — never another id.
            _now = Start.AddSeconds(3);
            var before = server.PresentedClientIds.Count;
            (await provider.RefreshAsync()).ShouldBeTrue();

            server.PresentedClientIds.Skip(before).ShouldBe(new[] { "agd_enroll" });
            NewStore().Read()!.Families!.Legacy!.ClientId.ShouldBe("agd_enroll");
        }

        [Fact]
        public async Task DecidedClientRejectionDuringFallback_IsNotPinnedAsTheRetryId()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "godot-cli");
            server.RejectClient("agd_enroll"); // a decided 400 invalid_client — nothing was lost
            using var provider = NewProvider(NewRefresher(server));

            (await provider.RefreshAsync()).ShouldBeFalse();
            server.PresentedClientIds.ShouldBe(new[] { "unity-mcp-plugin", "unity-mcp-cli", "agd_enroll" });

            // Past the refresher's 60 s window: the retry starts from the default again — it does
            // NOT lead with the rejected id (which would end every later attempt right there).
            _now = Start.AddSeconds(61);
            var before = server.PresentedClientIds.Count;
            (await provider.RefreshAsync()).ShouldBeFalse();
            server.PresentedClientIds[before].ShouldBe(ComponentClientId);
        }

        [Fact]
        public async Task BudgetExhausted_StopsProbing_WithoutAVerdict_AndTheNextAttemptContinues()
        {
            SeedCollapsedV1();
            var server = new MintServer().Mint(LegacyRefresh, "godot-cli");
            using var provider = NewProvider(NewRefresher(server));
            provider.ClientIdFallbackBudget = TimeSpan.Zero;

            (await provider.RefreshAsync()).ShouldBeFalse();

            server.PresentedClientIds.ShouldBe(new[] { ComponentClientId });
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn); // out of budget is not a verdict

            // The next attempt (normal budget) replays the refused id from the refresher's window
            // without a network call and reaches the untried ones.
            provider.ClientIdFallbackBudget = PluginCredentialProvider.DefaultClientIdFallbackBudget;
            (await provider.RefreshAsync()).ShouldBeTrue();

            server.PresentedClientIds.Count(id => id == ComponentClientId).ShouldBe(1);
            server.PresentedClientIds.Last().ShouldBe("godot-cli");
        }

        /// <summary>
        /// The AS's refresh rules that matter here (AI-Game-Dev-Server <c>grant_refresh_token</c>): a
        /// live token presented under another client id ⇒ <c>invalid_grant</c> /
        /// "client_id does not match the refresh token", nothing written; under its mint client ⇒
        /// rotation. Unknown tokens ⇒ <c>invalid_grant</c> / revoked.
        /// </summary>
        sealed class MintServer
        {
            readonly Dictionary<string, string> _mintClientByToken = new Dictionary<string, string>(StringComparer.Ordinal);
            readonly Dictionary<string, string> _headByClient = new Dictionary<string, string>(StringComparer.Ordinal);
            readonly HashSet<string> _failTransientlyOnce = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<string> _rejectedClients = new HashSet<string>(StringComparer.Ordinal);
            int _generation;

            public MintServer() => Handler = new RecordingHttpMessageHandler().RespondWith(Respond);

            public RecordingHttpMessageHandler Handler { get; }

            public IReadOnlyList<string> PresentedClientIds => Handler.Requests.Select(r => r.Form["client_id"]).ToList();

            public MintServer Mint(string refreshToken, string clientId)
            {
                _mintClientByToken[refreshToken] = clientId;
                return this;
            }

            public void FailTransientlyOnce(string clientId) => _failTransientlyOnce.Add(clientId);

            /// <summary>The AS does not know this client at all (a decided 400 invalid_client).</summary>
            public void RejectClient(string clientId) => _rejectedClients.Add(clientId);

            public string? HeadOf(string clientId) => _headByClient.TryGetValue(clientId, out var head) ? head : null;

            HttpResponseMessage Respond(RecordingHttpMessageHandler.RecordedRequest request)
            {
                var clientId = request.Form["client_id"];
                var token = request.Form["refresh_token"];

                if (_rejectedClients.Contains(clientId))
                    return Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}");
                if (!_mintClientByToken.TryGetValue(token, out var mintClient))
                    return Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"refresh token revoked\"}");
                if (!string.Equals(mintClient, clientId, StringComparison.Ordinal))
                    return Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"client_id does not match the refresh token\"}");
                if (_failTransientlyOnce.Remove(clientId))
                    return Json(HttpStatusCode.ServiceUnavailable, "{\"error\":\"temporarily_unavailable\"}");

                var successor = "RT-rotated-" + (++_generation);
                _mintClientByToken.Remove(token);
                _mintClientByToken[successor] = mintClient;
                _headByClient[mintClient] = successor;
                return Json(HttpStatusCode.OK,
                    "{\"access_token\":\"eyJ.NEW" + _generation + ".sig\",\"refresh_token\":\"" + successor + "\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
            }

            static HttpResponseMessage Json(HttpStatusCode status, string json)
                => RecordingHttpMessageHandler.JsonResponse(status, json);
        }
    }
}
