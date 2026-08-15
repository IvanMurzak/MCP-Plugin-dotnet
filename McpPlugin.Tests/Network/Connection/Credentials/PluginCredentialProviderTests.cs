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
    /// Coverage for the mcp-authorize b7 account credential provider: machine-store auto-adopt (the
    /// zero-button rule), proactive refresh before expiry, reactive refresh, and the sign-in-again state
    /// surfaced on refresh failure. The unified-machine-auth b3 behaviours (family/lock/double-checked
    /// refresh) are covered in <see cref="PluginCredentialProviderFamilyRefreshTests"/>.
    /// </summary>
    public sealed class PluginCredentialProviderTests : IDisposable
    {
        const string SeededAccessToken = "eyJ.SEEDED-ACCESS.aaa";
        const string SeededRefreshToken = "RT-SEEDED-bbb";
        const string RefreshedAccessToken = "eyJ.REFRESHED-ACCESS.ccc";
        const string RefreshedRefreshToken = "RT-REFRESHED-ddd";

        readonly string _baseDir;

        public PluginCredentialProviderTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-credprov-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        static MachineCredentials Seed(DateTimeOffset? expiresAt = null) => new MachineCredentials
        {
            AccessToken = SeededAccessToken,
            RefreshToken = SeededRefreshToken,
            ExpiresAt = expiresAt,
            ServerTarget = "https://ai-game.dev",
            Subject = "account-uuid-123",
        };

        // ── DoD: store auto-adopt — boot with a seeded store connects signed-in with ZERO calls to UI code. ──

        [Fact]
        public async Task AutoAdopt_SeededStore_IsSignedInWithoutRefresherInteraction()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure("must never be called"));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            // Signed in purely from the store read — no device flow, no network, no refresh.
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            provider.IsSignedIn.ShouldBeTrue();
            provider.ServerTarget.ShouldBe("https://ai-game.dev");
            provider.Subject.ShouldBe("account-uuid-123");

            var token = await provider.GetAccessTokenAsync();
            token.ShouldBe(SeededAccessToken);

            // The zero-button rule: nothing but the store was touched.
            refresher.Requests.ShouldBeEmpty();
            refresher.LegacyCalls.ShouldBe(0);
        }

        [Fact]
        public void AutoAdopt_EmptyStore_IsSignedOut()
        {
            using var provider = new PluginCredentialProvider(NewStore());

            provider.State.CurrentValue.ShouldBe(AuthState.SignedOut);
            provider.IsSignedIn.ShouldBeFalse();
        }

        [Fact]
        public async Task GetAccessToken_EmptyStore_ReturnsNull()
        {
            using var provider = new PluginCredentialProvider(NewStore());
            (await provider.GetAccessTokenAsync()).ShouldBeNull();
        }

        // ── DoD: expiry soak — a token within the skew window is proactively refreshed before use. ──

        [Fact]
        public async Task GetAccessToken_TokenNearExpiry_ProactivelyRefreshes()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddSeconds(10))); // within the 60s skew
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success(RefreshedAccessToken, RefreshedRefreshToken, DateTimeOffset.UtcNow.AddHours(1)));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            var token = await provider.GetAccessTokenAsync();

            token.ShouldBe(RefreshedAccessToken);
            var request = refresher.Requests.ShouldHaveSingleItem();
            request.RefreshToken.ShouldBe(SeededRefreshToken);
            request.ServerTarget.ShouldBe("https://ai-game.dev");
            refresher.LegacyCalls.ShouldBe(0); // the provider uses the family-aware API

            // The rotated token was persisted (a fresh store instance re-reads it) with identity preserved.
            var reread = NewStore().Read();
            reread!.AccessToken.ShouldBe(RefreshedAccessToken);
            reread.RefreshToken.ShouldBe(RefreshedRefreshToken);
            reread.Subject.ShouldBe("account-uuid-123");
        }

        [Fact]
        public async Task GetAccessToken_TokenFarFromExpiry_DoesNotRefresh()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(2)));
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure("must never be called"));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.GetAccessTokenAsync()).ShouldBe(SeededAccessToken);
            refresher.Requests.ShouldBeEmpty();
        }

        // ── DoD: refresh-on-reject — RefreshAsync mints + persists a new token. ──

        [Fact]
        public async Task RefreshAsync_Success_RotatesTokenAndStaysSignedIn()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success(RefreshedAccessToken, RefreshedRefreshToken, DateTimeOffset.UtcNow.AddHours(1)));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue();
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            (await provider.GetAccessTokenAsync()).ShouldBe(RefreshedAccessToken);
        }

        [Fact]
        public async Task RefreshAsync_Success_WithoutRotatedRefreshToken_KeepsExistingRefreshToken()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Success(RefreshedAccessToken)); // AS did not rotate the refresh token

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            (await provider.RefreshAsync()).ShouldBeTrue();
            NewStore().Read()!.RefreshToken.ShouldBe(SeededRefreshToken);
        }

        // ── Failure-state split (review B2, 03 F3.5/F9): SignInRequired is DEAD-FAMILY-only.
        //    A transient failure (AS unreachable, 5xx, timeout) keeps the machine signed in and
        //    retries later — offline self-heals silently (F9), no sign-in prompt. ──

        [Fact]
        public async Task RefreshAsync_TransientFailure_StaysSignedIn_DoesNotSurfaceSignInRequired()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(TokenRefreshResult.Failure("as unreachable")); // Transient by default

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse(); // this attempt failed — retry-later semantics
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            signInRequiredFired.ShouldBeFalse();
            (await provider.GetAccessTokenAsync()).ShouldBe(SeededAccessToken); // existing credential kept
        }

        [Fact]
        public async Task RefreshAsync_InvalidGrant_SurfacesSignInRequired()
        {
            // The one failure kind that reaches SignInRequired: invalid_grant, confirmed dead by
            // the post-failure re-read (nobody else rotated the family).
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(
                TokenRefreshResult.Failure("refresh token expired", TokenRefreshFailureKind.InvalidGrant));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            signInRequiredFired.ShouldBeTrue();
        }

        [Fact]
        public async Task RefreshAsync_ThrowingRefresher_StaysSignedIn_WithoutLeaking()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new FakeTokenRefresher(_ => throw new InvalidOperationException("network down"));

            using var provider = new PluginCredentialProvider(NewStore(), refresher);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn); // an exception is transient — retry later
            signInRequiredFired.ShouldBeFalse();
        }

        [Fact]
        public async Task RefreshAsync_NoRefresher_SurfacesSignInRequired()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(NewStore(), refresher: null);

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
        }

        // ── Sign-out clears the store and resets state. ──

        [Fact]
        public void SignOut_DeletesStoreAndResetsState()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            using var provider = new PluginCredentialProvider(NewStore());

            provider.SignOut();

            provider.State.CurrentValue.ShouldBe(AuthState.SignedOut);
            provider.IsSignedIn.ShouldBeFalse();
            NewStore().Exists.ShouldBeFalse();
        }

        [Fact]
        public void Adopt_SignsInAndPersists()
        {
            using var provider = new PluginCredentialProvider(NewStore());
            provider.State.CurrentValue.ShouldBe(AuthState.SignedOut);

            provider.Adopt(new MachineCredentials
            {
                AccessToken = RefreshedAccessToken,
                RefreshToken = RefreshedRefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                ServerTarget = "https://ai-game.dev",
            });

            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            NewStore().Read()!.AccessToken.ShouldBe(RefreshedAccessToken);
        }

        // ── The Adopt/Write in-place-mutation seam (unified-machine-auth b1 review A1, pinned). ──

        [Fact]
        public void Adopt_ReliesOnWriteMutatingItsArgumentIntoFamilyShape()
        {
            // MachineCredentialStore.Write() normalizes its ARGUMENT in place (v1 adoption →
            // families.legacy, version upgrade, mirror stamp) and Adopt() keeps that same mutated
            // instance as the provider's current credential. The provider's family-aware refresh
            // path depends on this — so the seam is pinned here instead of being refactored away
            // silently (b1 review A1). If Write() ever stops mutating its argument, this test goes
            // red and the provider must clone-and-adopt explicitly.
            var adopted = new MachineCredentials
            {
                AccessToken = RefreshedAccessToken,
                RefreshToken = RefreshedRefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            };

            using var provider = new PluginCredentialProvider(NewStore());
            provider.Adopt(adopted);

            // The caller's own instance became family-shaped (v2) during the write.
            adopted.Families.ShouldNotBeNull();
            adopted.Families!.Legacy.ShouldNotBeNull();
            adopted.Families.Legacy!.AccessToken.ShouldBe(RefreshedAccessToken);
            adopted.Version.ShouldBe(MachineCredentials.CurrentVersion);
        }
    }
}
