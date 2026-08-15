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
using System.IO;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// The unified-machine-auth b3 provider behaviours (design 04 §3, 03 F3): the lock-integrated
    /// double-checked refresh path, family context handed to the refresher, the dead-family
    /// (<c>invalid_grant</c>) verdict with its post-failure re-read, unreadable-store-as-busy
    /// (b1 review A3), the §3.7 legacy rewrite, and lock-busy-as-transient.
    /// </summary>
    public sealed class PluginCredentialProviderFamilyRefreshTests : IDisposable
    {
        const string StoredClientId = "app-dcr-4f2a9c31";
        const string AgentClientId = "godot-mcp-plugin";
        const string ServerTarget = "https://ai-game.dev";

        readonly string _baseDir;

        public PluginCredentialProviderFamilyRefreshTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-credfam-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        MachineCredentials SeedV2(DateTimeOffset? pluginExpiresAt = null)
        {
            var credentials = new MachineCredentials
            {
                ServerTarget = ServerTarget,
                Subject = "usr_123",
                Families = new MachineCredentialFamilies
                {
                    Agent = new MachineCredentialFamily
                    {
                        AccessToken = "eyJ.AGENT.sig",
                        RefreshToken = "RT-agent-aaa",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                        ClientId = AgentClientId,
                        Scope = "mcp:agent",
                    },
                    Plugin = new MachineCredentialFamily
                    {
                        AccessToken = "eyJ.PLUGIN.sig",
                        RefreshToken = "RT-plugin-aaa",
                        ExpiresAt = pluginExpiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
                        ClientId = StoredClientId,
                        Scope = "mcp:plugin",
                    },
                },
            };
            NewStore().Write(credentials);
            return credentials;
        }

        // ── Family context: the refresher gets the STORED clientId (04 §3.2). ──

        [Fact]
        public async Task Refresh_HandsTheRefresherThePluginFamilysStoredClientId()
        {
            SeedV2();
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success("eyJ.PLUGIN2.sig", "RT-plugin-bbb", DateTimeOffset.UtcNow.AddHours(1)));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue();

            var request = refresher.Requests.ShouldHaveSingleItem();
            request.ClientId.ShouldBe(StoredClientId); // never the component default, never the agent family's
            request.RefreshToken.ShouldBe("RT-plugin-aaa");
            request.ServerTarget.ShouldBe(ServerTarget);
            refresher.LegacyCalls.ShouldBe(0);
        }

        [Fact]
        public async Task Refresh_PluginFamilySuccess_NeverTouchesTheAgentFamily()
        {
            SeedV2();
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success("eyJ.PLUGIN2.sig", "RT-plugin-bbb", DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue();

            var reread = NewStore().Read()!;
            reread.Families!.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN2.sig");
            reread.Families.Plugin.RefreshToken.ShouldBe("RT-plugin-bbb");
            reread.Families.Plugin.ClientId.ShouldBe(StoredClientId); // identity preserved through rotation
            reread.Families.Plugin.Scope.ShouldBe("mcp:plugin");
            // The agent family is untouched by a plugin-plane rotation.
            reread.Families.Agent!.AccessToken.ShouldBe("eyJ.AGENT.sig");
            reread.Families.Agent.RefreshToken.ShouldBe("RT-agent-aaa");
            reread.Families.Agent.ClientId.ShouldBe(AgentClientId);
            // And the v1 mirror follows the plugin family (write contract).
            reread.AccessToken.ShouldBe("eyJ.PLUGIN2.sig");
        }

        // ── Double-checked refresh (03 F3.2): a peer's rotation is adopted without a network call. ──

        [Fact]
        public async Task Refresh_PeerAlreadyRotatedTheStore_AdoptsWithoutANetworkCall()
        {
            SeedV2();
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure("must not be called"));
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            // A peer process rotates the plugin family AFTER our provider booted (its in-memory
            // token is now the predecessor the server just rejected).
            var peer = NewStore().Read()!;
            peer.Families!.Plugin!.AccessToken = "eyJ.PEER-FRESH.sig";
            peer.Families.Plugin.RefreshToken = "RT-plugin-peer";
            peer.Families.Plugin.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            NewStore().Write(peer);

            (await provider.RefreshAsync()).ShouldBeTrue();

            refresher.Requests.ShouldBeEmpty(); // the re-read under the lock satisfied the refresh
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.PEER-FRESH.sig");
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
        }

        // ── invalid_grant (04 §3.5 / 03 F3.5): post-failure re-read, then the dead-family verdict. ──

        [Fact]
        public async Task Refresh_InvalidGrant_WithNoPeerRotation_IsDeadFamily_SignInRequired_OneAttempt()
        {
            SeedV2();
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Failure("invalid_grant: revoked", TokenRefreshFailureKind.InvalidGrant));
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeFalse();

            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            refresher.Requests.Count.ShouldBe(1); // never loops

            // Never delete other families, never delete the store (03 F3.5).
            var reread = NewStore().Read()!;
            reread.Families!.Agent.ShouldNotBeNull();
            reread.Families.Agent!.RefreshToken.ShouldBe("RT-agent-aaa");
            reread.Families.Plugin.ShouldNotBeNull(); // even the dead family's material is preserved on disk
        }

        [Fact]
        public async Task Refresh_InvalidGrant_ButAPeerRotatedMeanwhile_AdoptsThePeersTokens()
        {
            SeedV2();
            var store = NewStore();
            var refresher = new FakeTokenRefresher(_ =>
            {
                // Simulate the race 03 F3.5 describes: OUR refresh token was reuse-revoked because
                // a peer legitimately rotated it. The peer's write lands while our HTTP call is in
                // flight (we hold the file lock, but this peer had rotated between our re-read and
                // the AS answering — the re-read window the post-failure re-read exists for).
                var peer = store.Read()!;
                peer.Families!.Plugin!.AccessToken = "eyJ.RACER.sig";
                peer.Families.Plugin.RefreshToken = "RT-plugin-racer";
                store.Write(peer);
                return TokenRefreshResult.Failure("invalid_grant", TokenRefreshFailureKind.InvalidGrant);
            });
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue(); // the racer's rotation IS a successful refresh

            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.RACER.sig");
        }

        // ── Unreadable store mid-refresh = busy/retry (b1 review A3) — never signed-out, never overwrite. ──

        [Fact]
        public async Task Refresh_StoreUnreadableAtReread_IsBusy_NotSignInRequired_AndStoreIsPreserved()
        {
            SeedV2();
            using var provider = new PluginCredentialProvider(
                NewStore(),
                new FakeTokenRefresher(TokenRefreshResult.Failure("must not be called")));

            // Corrupt the store AFTER boot: the refresh-time re-read now yields Unreadable.
            File.WriteAllBytes(NewStore().CredentialsPath, new byte[] { 0x01, 0x02, 0x03 });
            var corrupted = File.ReadAllBytes(NewStore().CredentialsPath);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();

            // Busy semantics: no sign-in-required, the provider stays signed in on its in-memory
            // token, and the unreadable file was neither deleted nor overwritten.
            signInRequiredFired.ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            File.ReadAllBytes(NewStore().CredentialsPath).ShouldBe(corrupted);
        }

        [Fact]
        public void Boot_UnreadableStore_IsSignInRequired_NotSignedOut_AndFileIsPreserved()
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllBytes(Path.Combine(_baseDir, MachineCredentialStore.CredentialsFileName), new byte[] { 0x01 });

            using var provider = new PluginCredentialProvider(NewStore());

            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired); // 03 F11.4: distinct from a clean machine
            NewStore().Exists.ShouldBeTrue(); // never deleted
        }

        // ── Lock integration (04 §2): busy is transient; the peer's lock survives. ──

        [Fact]
        public async Task Refresh_LockHeldByAPeer_FailsAsBusy_NoNetworkCall_NoStateChange()
        {
            SeedV2();
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure("must not be called"));

            // A short-budget lock (test seam) so "busy" resolves in milliseconds, not 75 s.
            var shortBudgetLock = new MachineCredentialLock(_baseDir, hostId: null,
                acquireBudgetMs: 200, staleMs: MachineCredentialLock.LOCK_STALE_MS,
                foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS);
            using var provider = new PluginCredentialProvider(
                NewStore(), refresher, credentialLock: shortBudgetLock);

            // A live peer holds the lock.
            var peerLock = new MachineCredentialLock(_baseDir);
            using var peerHold = peerLock.TryAcquire();
            peerHold.ShouldNotBeNull();

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();

            refresher.Requests.ShouldBeEmpty(); // never refreshes lock-free (D9 REVISED)
            signInRequiredFired.ShouldBeFalse(); // busy ≠ auth failure
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            File.Exists(peerLock.LockPath).ShouldBeTrue(); // the peer's lock was not stolen
        }

        [Fact]
        public async Task Refresh_LockTakenOverMidCall_SkipsTheDiskWrite_ButKeepsTheTokenInMemory()
        {
            SeedV2(pluginExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
            var lockPath = Path.Combine(_baseDir, MachineCredentialLock.LockFileName);
            var refresher = new FakeTokenRefresher(_ =>
            {
                // Simulate a (protocol-violating / clock-skewed) takeover during the HTTP call:
                // our lock file is replaced by a foreign one. IsStillOwned() must catch this
                // immediately before the write (b2 review A1).
                File.WriteAllText(lockPath, "{\"pid\":999,\"hostId\":\"intruder\",\"nonce\":\"ff\"}");
                return TokenRefreshResult.Success("eyJ.FRESH.sig", "RT-plugin-fresh", DateTimeOffset.UtcNow.AddHours(1));
            });
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue(); // in-memory refresh still succeeds

            (await provider.GetAccessTokenAsync()).ShouldBe("eyJ.FRESH.sig");
            // But the store was NOT written by a writer that no longer holds the lock.
            NewStore().Read()!.Families!.Plugin!.AccessToken.ShouldBe("eyJ.PLUGIN.sig");
        }

        // ── A peer's machine-wide sign-out is adopted quietly (03 F6.3). ──

        [Fact]
        public async Task Refresh_StoreDeletedByAPeer_AdoptsSignedOut_WithoutSignInRequired()
        {
            SeedV2();
            using var provider = new PluginCredentialProvider(
                NewStore(),
                new FakeTokenRefresher(TokenRefreshResult.Failure("must not be called")));

            NewStore().Delete(); // the peer's F6 sign-out

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();

            provider.State.CurrentValue.ShouldBe(AuthState.SignedOut);
            signInRequiredFired.ShouldBeFalse();
        }

        // ── Legacy family (04 §3.7): component-default id at the refresher, v2 rewrite on success. ──

        [Fact]
        public async Task Refresh_LegacyV1Document_HandsNullClientIdToTheRefresher_AndRewritesAsV2OnSuccess()
        {
            // Plant a REAL v1 document (top-level fields only), as an old writer left it.
            NewStore().WritePlaintextJson(
                "{\"version\":1,\"accessToken\":\"eyJ.V1.sig\",\"refreshToken\":\"RT-v1-aaa\"," +
                "\"expiresAt\":\"2026-01-01T00:00:00+00:00\",\"serverTarget\":\"https://ai-game.dev\"}");

            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success("eyJ.V2.sig", "RT-v2-bbb", DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue();

            // The refresher saw a legacy family: unknown clientId (⇒ it presents its component default).
            refresher.Requests.ShouldHaveSingleItem().ClientId.ShouldBeNull();

            // §3.7: the document was rewritten as v2 families.legacy (+ mirror, it being the sole
            // plugin-plane credential).
            var raw = NewStore().ReadPlaintextJson()!;
            raw.ShouldContain("\"version\": 2");
            var reread = NewStore().Read()!;
            reread.Families!.Legacy!.AccessToken.ShouldBe("eyJ.V2.sig");
            reread.Families.Legacy.RefreshToken.ShouldBe("RT-v2-bbb");
            reread.Families.Legacy.ClientId.ShouldBeNull(); // still unknown by definition
            reread.AccessToken.ShouldBe("eyJ.V2.sig"); // the v1 mirror
        }
    }
}
