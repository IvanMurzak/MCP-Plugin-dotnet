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
using System.Runtime.InteropServices;
using System.Text;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Coverage for the shared machine credential store: read/write/rotate/delete round-trips, the
    /// co-located JWKS cache, and the per-OS at-rest protection — <c>0600</c> file / <c>0700</c>
    /// directory on POSIX (exercised on Linux CI) and DPAPI encryption on Windows (exercised locally).
    /// </summary>
    public sealed class MachineCredentialStoreTests : IDisposable
    {
        private const string SecretAccessToken = "eyJ.SECRET-ACCESS-TOKEN.zzz";
        private const string SecretRefreshToken = "RT-SECRET-REFRESH-abcdef";

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private readonly string _baseDir;

        public MachineCredentialStoreTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-store-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        private static MachineCredentials SampleCredentials() => new MachineCredentials
        {
            AccessToken = SecretAccessToken,
            RefreshToken = SecretRefreshToken,
            ExpiresAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ServerTarget = "https://ai-game.dev",
            Subject = "account-uuid-123",
        };

        [Fact]
        public void Read_ReturnsNull_WhenNoCredentials()
        {
            NewStore().Read().ShouldBeNull();
        }

        [Fact]
        public void Write_ThenRead_RoundTripsAllFields()
        {
            var store = NewStore();
            store.Write(SampleCredentials());

            var read = store.Read();
            read.ShouldNotBeNull();
            read!.AccessToken.ShouldBe(SecretAccessToken);
            read.RefreshToken.ShouldBe(SecretRefreshToken);
            read.ExpiresAt.ShouldBe(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
            read.ServerTarget.ShouldBe("https://ai-game.dev");
            read.Subject.ShouldBe("account-uuid-123");
            read.Version.ShouldBe(1);
        }

        [Fact]
        public void Rotate_UpdatesTokens_PreservesIdentityFields()
        {
            var store = NewStore();
            store.Write(SampleCredentials());

            var rotated = store.Rotate(
                accessToken: "NEW-ACCESS",
                refreshToken: "NEW-REFRESH",
                expiresAt: new DateTimeOffset(2031, 6, 7, 8, 9, 10, TimeSpan.Zero));

            rotated.AccessToken.ShouldBe("NEW-ACCESS");
            rotated.RefreshToken.ShouldBe("NEW-REFRESH");

            var read = store.Read();
            read!.AccessToken.ShouldBe("NEW-ACCESS");
            read.RefreshToken.ShouldBe("NEW-REFRESH");
            read.ExpiresAt.ShouldBe(new DateTimeOffset(2031, 6, 7, 8, 9, 10, TimeSpan.Zero));
            // Identity fields survive a rotation.
            read.ServerTarget.ShouldBe("https://ai-game.dev");
            read.Subject.ShouldBe("account-uuid-123");
        }

        [Fact]
        public void Rotate_OnEmptyStore_CreatesCredentials()
        {
            var store = NewStore();
            store.Read().ShouldBeNull();

            store.Rotate("A", "R");

            var read = store.Read();
            read.ShouldNotBeNull();
            read!.AccessToken.ShouldBe("A");
            read.RefreshToken.ShouldBe("R");
        }

        [Fact]
        public void Delete_RemovesCredentials()
        {
            var store = NewStore();
            store.Write(SampleCredentials());
            store.Exists.ShouldBeTrue();

            store.Delete();

            store.Exists.ShouldBeFalse();
            store.Read().ShouldBeNull();
            Should.NotThrow(() => store.Delete()); // idempotent
        }

        [Fact]
        public void JwksCache_RoundTrips_AndIsSeparateFromCredentials()
        {
            var store = NewStore();
            const string jwks = "{\"keys\":[{\"kid\":\"k1\",\"kty\":\"EC\"}]}";

            store.ReadJwksCache().ShouldBeNull();
            store.WriteJwksCache(jwks);

            store.ReadJwksCache().ShouldBe(jwks);
            store.JwksCachePath.ShouldEndWith("jwks-cache.json");
            store.JwksCachePath.ShouldNotBe(store.CredentialsPath);
        }

        [Fact]
        public void PosixPermissions_CredentialsAre0600_DirectoryIs0700()
        {
            if (OperatingSystem.IsWindows())
                return; // POSIX-only assertion; exercised on Linux CI. (analyzer-recognized guard for CA1416)

            var store = NewStore();
            store.Write(SampleCredentials());

            var fileMode = File.GetUnixFileMode(store.CredentialsPath);
            fileMode.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600 — no group/other bits

            var dirMode = File.GetUnixFileMode(store.BaseDirectory);
            dirMode.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 0700
        }

        [Fact]
        public void Posix_StoresPlaintextJson_ProtectedByPermissions()
        {
            if (IsWindows)
                return; // POSIX stores plaintext (guarded by 0600); Windows uses DPAPI — see below.

            var store = NewStore();
            store.Write(SampleCredentials());

            var onDisk = File.ReadAllText(store.CredentialsPath);
            onDisk.ShouldContain(SecretAccessToken); // plaintext JSON on POSIX
        }

        [Fact]
        public void Windows_Dpapi_EncryptsCredentialsAtRest()
        {
            if (!IsWindows)
                return; // Windows-only assertion; exercised on a Windows dev machine.

            var store = NewStore();
            store.Write(SampleCredentials());

            var onDiskBytes = File.ReadAllBytes(store.CredentialsPath);
            var onDiskText = Encoding.UTF8.GetString(onDiskBytes);

            // The secret must NOT appear in plaintext on disk (DPAPI-encrypted).
            onDiskText.ShouldNotContain(SecretAccessToken);
            onDiskText.ShouldNotContain(SecretRefreshToken);

            // ...yet a round-trip through the store still recovers it.
            store.Read()!.AccessToken.ShouldBe(SecretAccessToken);
        }

        [Fact]
        public void DefaultBaseDirectory_IsUnderUserProfile_DotAiGameDev()
        {
            var store = new MachineCredentialStore();
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ai-game-dev");
            store.BaseDirectory.ShouldBe(expected);
        }
    }
}
