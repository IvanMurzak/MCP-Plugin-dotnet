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
using System.Collections.Generic;
using System.IO;
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
    /// Machine-wide sign-out (unified-machine-auth design 03 F6, task b3): RFC 7009 revocation of
    /// every family — stored <c>clientId</c>s, component default for legacy (F6.2) — with bounded
    /// retries, then the 04 §2 lock-protocol delete path; offline sign-out still deletes locally
    /// (F6.4). Plus the wire contract of the production <see cref="TokenRevocationClient"/>.
    /// </summary>
    public sealed class MachineWideSignOutTests : IDisposable
    {
        const string ComponentClientId = "unity-mcp-plugin";
        const string ServerTarget = "https://ai-game.dev";

        readonly string _baseDir;

        public MachineWideSignOutTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-signout-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);
        MachineCredentialLock NewLock() => new MachineCredentialLock(_baseDir);

        void SeedAllThreeFamilies()
        {
            NewStore().Write(new MachineCredentials
            {
                ServerTarget = ServerTarget,
                Subject = "usr_123",
                Families = new MachineCredentialFamilies
                {
                    Agent = new MachineCredentialFamily { AccessToken = "eyJ.A.s", RefreshToken = "RT-agent", ClientId = "app-dcr-1234" },
                    Plugin = new MachineCredentialFamily { AccessToken = "eyJ.P.s", RefreshToken = "RT-plugin", ClientId = "godot-mcp-plugin" },
                    Legacy = new MachineCredentialFamily { AccessToken = "eyJ.L.s", RefreshToken = "RT-legacy" }, // no clientId, by definition
                },
            });
        }

        sealed class FakeRevocationClient : ITokenRevocationClient
        {
            readonly Func<string, bool> _onRevoke;
            public readonly List<(string Token, string? ClientId, string? ServerTarget)> Calls
                = new List<(string, string?, string?)>();

            public FakeRevocationClient(Func<string, bool>? onRevoke = null) { _onRevoke = onRevoke ?? (_ => true); }

            public Task<bool> RevokeAsync(string token, string? clientId, string? serverTarget, CancellationToken cancellationToken = default)
            {
                Calls.Add((token, clientId, serverTarget));
                return Task.FromResult(_onRevoke(token));
            }
        }

        // ── The full F6 sequence. ──

        [Fact]
        public async Task SignOut_RevokesEveryFamilyWithItsOwnClientId_ThenDeletesTheStore()
        {
            SeedAllThreeFamilies();
            var revocation = new FakeRevocationClient();

            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), NewLock(), revocation, ComponentClientId);

            result.StoreDeleted.ShouldBeTrue();
            result.Busy.ShouldBeFalse();
            result.FamiliesRevoked.ShouldBe(3);
            result.RevocationsFailed.ShouldBe(0);

            // Each family presented ITS stored clientId; the legacy family (unknown id) presented
            // the component default (F6.2).
            revocation.Calls.Count.ShouldBe(3);
            revocation.Calls.ShouldContain(c => c.Token == "RT-agent" && c.ClientId == "app-dcr-1234");
            revocation.Calls.ShouldContain(c => c.Token == "RT-plugin" && c.ClientId == "godot-mcp-plugin");
            revocation.Calls.ShouldContain(c => c.Token == "RT-legacy" && c.ClientId == ComponentClientId);
            revocation.Calls.ForEach(c => c.ServerTarget.ShouldBe(ServerTarget));

            NewStore().Exists.ShouldBeFalse();                    // unlinked via the lock delete path
            File.Exists(NewLock().LockPath).ShouldBeFalse();      // and the lock was released
        }

        // ── Offline sign-out (F6.4): bounded retries, then delete anyway. ──

        [Fact]
        public async Task SignOut_RevocationKeepsFailing_RetriesBounded_ThenStillDeletesLocally()
        {
            SeedAllThreeFamilies();
            var revocation = new FakeRevocationClient(_ => false); // AS unreachable

            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), NewLock(), revocation, ComponentClientId);

            result.StoreDeleted.ShouldBeTrue(); // offline sign-out still signs the machine out
            result.FamiliesRevoked.ShouldBe(0);
            result.RevocationsFailed.ShouldBe(3);
            // Bounded: exactly RevokeAttemptsPerFamily attempts per family, never unbounded.
            revocation.Calls.Count.ShouldBe(3 * MachineWideSignOut.RevokeAttemptsPerFamily);
            NewStore().Exists.ShouldBeFalse();
        }

        [Fact]
        public async Task SignOut_TransientRevocationFailure_SucceedsWithinTheRetryBudget()
        {
            SeedAllThreeFamilies();
            var attemptsByToken = new Dictionary<string, int>();
            var revocation = new FakeRevocationClient(token =>
            {
                attemptsByToken[token] = attemptsByToken.TryGetValue(token, out var n) ? n + 1 : 1;
                return attemptsByToken[token] >= 2; // first attempt fails, second succeeds
            });

            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), NewLock(), revocation, ComponentClientId);

            result.FamiliesRevoked.ShouldBe(3);
            result.RevocationsFailed.ShouldBe(0);
        }

        // ── The delete path is lock-protocol only: busy ⇒ store untouched. ──

        [Fact]
        public async Task SignOut_LockBusy_RevokesButDoesNotDeleteTheStore()
        {
            SeedAllThreeFamilies();
            var revocation = new FakeRevocationClient();
            var shortBudgetLock = new MachineCredentialLock(_baseDir, hostId: null,
                acquireBudgetMs: 200, staleMs: MachineCredentialLock.LOCK_STALE_MS,
                foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS);

            using var peerHold = NewLock().TryAcquire();
            peerHold.ShouldNotBeNull();

            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), shortBudgetLock, revocation, ComponentClientId);

            result.StoreDeleted.ShouldBeFalse();
            result.Busy.ShouldBeTrue();
            NewStore().Exists.ShouldBeTrue(); // never unlinked outside the lock protocol
        }

        [Fact]
        public async Task SignOut_EmptyStore_IsAQuietNoOp()
        {
            var revocation = new FakeRevocationClient();

            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), NewLock(), revocation, ComponentClientId);

            result.StoreDeleted.ShouldBeTrue(); // idempotent — the machine ends signed out
            revocation.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task SignOut_UnreadableStore_SkipsRevocation_ButStillDeletes()
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllBytes(Path.Combine(_baseDir, MachineCredentialStore.CredentialsFileName), new byte[] { 0x01 });
            var revocation = new FakeRevocationClient();

            // Sign-out IS the explicit user action that authorizes removing an unreadable store (04 §1).
            var result = await MachineWideSignOut.SignOutAsync(
                NewStore(), NewLock(), revocation, ComponentClientId);

            result.StoreDeleted.ShouldBeTrue();
            revocation.Calls.ShouldBeEmpty(); // its tokens could not be read
            NewStore().Exists.ShouldBeFalse();
        }

        // ── The production RFC 7009 client's wire contract. ──

        [Fact]
        public async Task RevocationClient_PostsTokenHintAndClientId_ToTheRevokeEndpoint()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.OK, "{}");
            var client = new TokenRevocationClient(ServerTarget, new HttpClient(handler));

            var acknowledged = await client.RevokeAsync("RT-x", "godot-mcp-plugin", ServerTarget);

            acknowledged.ShouldBeTrue();
            var request = handler.Requests.ShouldHaveSingleItem();
            request.Url.ShouldBe("https://ai-game.dev/oauth/revoke");
            request.Form["token"].ShouldBe("RT-x");
            request.Form["token_type_hint"].ShouldBe("refresh_token");
            request.Form["client_id"].ShouldBe("godot-mcp-plugin");
        }

        [Fact]
        public async Task RevocationClient_ServerError_FailsSoft()
        {
            var handler = new RecordingHttpMessageHandler().RespondWith(HttpStatusCode.ServiceUnavailable, "{}");
            var client = new TokenRevocationClient(ServerTarget, new HttpClient(handler));

            (await client.RevokeAsync("RT-x", null, ServerTarget)).ShouldBeFalse();
        }
    }
}
