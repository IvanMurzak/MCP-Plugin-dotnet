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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>Protection / atomicity / resilience of <see cref="ProjectKeyStore"/> (project-keys contract §6).</summary>
    public sealed class ProjectKeyStoreTests : IDisposable
    {
        private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "agd-pk-store-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private static ProjectKeyEntry Entry(string pin = "aabbccdd", string key = "agd_pk_secret-value") => new ProjectKeyEntry
        {
            Key = key, KeyId = "pk_1", Pin = pin, Issuer = "https://ai-game.dev", Sub = "usr_1", Engine = "unity", CreatedAt = "2026-09-23T00:00:00Z",
        };

        [Fact]
        public void FileLivesBesideCredentialsJson_ButIsASeparateFile()
        {
            var store = new ProjectKeyStore(_baseDir);
            store.FilePath.ShouldBe(Path.Combine(_baseDir, "project-keys.json"));
            store.FilePath.ShouldNotBe(new MachineCredentialStore(_baseDir).CredentialsPath);
        }

        [Fact]
        public void PutThenGet_RoundTrips_AndRemoveDeletesOnlyThatEntry()
        {
            var store = new ProjectKeyStore(_baseDir);
            store.Put(Entry("aabbccdd", "agd_pk_a"));
            store.Put(Entry("11223344", "agd_pk_b"));

            store.Get("https://ai-game.dev/", "AABBCCDD")!.Key.ShouldBe("agd_pk_a");
            store.Remove("https://ai-game.dev", "aabbccdd").ShouldBeTrue();
            store.Get("https://ai-game.dev", "aabbccdd").ShouldBeNull();
            store.Get("https://ai-game.dev", "11223344")!.Key.ShouldBe("agd_pk_b");
        }

        [Fact]
        public void Write_IsAtomic_NoTempSiblingsLeftBehind()
        {
            var store = new ProjectKeyStore(_baseDir);
            for (var i = 0; i < 5; i++)
                store.Put(Entry(key: "agd_pk_" + i));

            Directory.GetFiles(_baseDir).Select(Path.GetFileName).ShouldBe(new[] { "project-keys.json" });
        }

        [Fact]
        public void Posix_FileIs0600_DirectoryIs0700()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return; // DPAPI covers Windows (next test).
            new ProjectKeyStore(_baseDir).Put(Entry());

            File.GetUnixFileMode(Path.Combine(_baseDir, "project-keys.json"))
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(_baseDir)
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        [Fact]
        public void OnDisk_Bytes_UseTheCredentialsJsonEnvelope()
        {
            // Windows: the file is the raw DPAPI blob of the UTF-8 JSON — decrypting it with the SAME helper
            // credentials.json uses yields the plaintext, and the secret never appears in the raw bytes.
            // POSIX: the file IS the plaintext. Either way: UnprotectBytes(file) == plaintext.
            var store = new ProjectKeyStore(_baseDir);
            store.Put(Entry(key: "agd_pk_envelope-probe"));

            var raw = File.ReadAllBytes(store.FilePath);
            var plaintext = Encoding.UTF8.GetString(MachineCredentialStore.UnprotectBytes(raw));
            plaintext.ShouldContain("agd_pk_envelope-probe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Encoding.UTF8.GetString(raw).ShouldNotContain("agd_pk_envelope-probe");
        }

        [Fact]
        public void CorruptFile_ReadsAsMiss_AndPutRecovers()
        {
            Directory.CreateDirectory(_baseDir);
            var store = new ProjectKeyStore(_baseDir);
            File.WriteAllBytes(store.FilePath, new byte[] { 0x7b, 0x00, 0xff, 0x13 });

            store.Get("https://ai-game.dev", "aabbccdd").ShouldBeNull();
            store.Put(Entry());
            store.Get("https://ai-game.dev", "aabbccdd")!.Key.ShouldBe("agd_pk_secret-value");
        }

        [Fact]
        public void Put_RejectsEmptyKeyAndBadPin()
        {
            var store = new ProjectKeyStore(_baseDir);
            Should.Throw<ArgumentException>(() => store.Put(Entry(key: "")));
            Should.Throw<ArgumentException>(() => store.Put(Entry(pin: "xyz")));
            File.Exists(store.FilePath).ShouldBeFalse();
        }
    }
}
