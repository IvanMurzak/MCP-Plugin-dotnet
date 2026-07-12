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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Moq;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// Coverage for the mcp-authorize b7 account credential provider: machine-store auto-adopt (the
    /// zero-button rule), proactive refresh before expiry, reactive refresh, and the sign-in-again state
    /// surfaced on refresh failure.
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
            var refresher = new Mock<ITokenRefresher>(MockBehavior.Strict);

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            // Signed in purely from the store read — no device flow, no network, no refresh.
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            provider.IsSignedIn.ShouldBeTrue();
            provider.ServerTarget.ShouldBe("https://ai-game.dev");
            provider.Subject.ShouldBe("account-uuid-123");

            var token = await provider.GetAccessTokenAsync();
            token.ShouldBe(SeededAccessToken);

            // The zero-button rule: nothing but the store was touched (a strict mock proves the refresher
            // was never invoked during boot + token fetch of a still-valid credential).
            refresher.VerifyNoOtherCalls();
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
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(SeededRefreshToken, "https://ai-game.dev", It.IsAny<CancellationToken>()))
                .ReturnsAsync(TokenRefreshResult.Success(RefreshedAccessToken, RefreshedRefreshToken, DateTimeOffset.UtcNow.AddHours(1)));

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            var token = await provider.GetAccessTokenAsync();

            token.ShouldBe(RefreshedAccessToken);
            refresher.Verify(r => r.RefreshAsync(SeededRefreshToken, "https://ai-game.dev", It.IsAny<CancellationToken>()), Times.Once);

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
            var refresher = new Mock<ITokenRefresher>();

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            (await provider.GetAccessTokenAsync()).ShouldBe(SeededAccessToken);
            refresher.Verify(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── DoD: refresh-on-reject — RefreshAsync mints + persists a new token. ──

        [Fact]
        public async Task RefreshAsync_Success_RotatesTokenAndStaysSignedIn()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(SeededRefreshToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TokenRefreshResult.Success(RefreshedAccessToken, RefreshedRefreshToken, DateTimeOffset.UtcNow.AddHours(1)));

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            (await provider.RefreshAsync()).ShouldBeTrue();
            provider.State.CurrentValue.ShouldBe(AuthState.SignedIn);
            (await provider.GetAccessTokenAsync()).ShouldBe(RefreshedAccessToken);
        }

        [Fact]
        public async Task RefreshAsync_Success_WithoutRotatedRefreshToken_KeepsExistingRefreshToken()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(SeededRefreshToken, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TokenRefreshResult.Success(RefreshedAccessToken)); // AS did not rotate the refresh token

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            (await provider.RefreshAsync()).ShouldBeTrue();
            NewStore().Read()!.RefreshToken.ShouldBe(SeededRefreshToken);
        }

        // ── DoD: refresh failure surfaces "sign in again". ──

        [Fact]
        public async Task RefreshAsync_Failure_SurfacesSignInRequired()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(TokenRefreshResult.Failure("refresh token expired"));

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            var signInRequiredFired = false;
            using var _ = provider.OnSignInRequired.Subscribe(__ => { signInRequiredFired = true; });

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
            signInRequiredFired.ShouldBeTrue();
        }

        [Fact]
        public async Task RefreshAsync_ThrowingRefresher_SurfacesSignInRequired_WithoutLeaking()
        {
            NewStore().Write(Seed(expiresAt: DateTimeOffset.UtcNow.AddHours(1)));
            var refresher = new Mock<ITokenRefresher>();
            refresher
                .Setup(r => r.RefreshAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("network down"));

            using var provider = new PluginCredentialProvider(NewStore(), refresher.Object);

            (await provider.RefreshAsync()).ShouldBeFalse();
            provider.State.CurrentValue.ShouldBe(AuthState.SignInRequired);
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
    }
}
