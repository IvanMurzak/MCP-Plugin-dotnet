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
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The v2 store contract (design 04 §1): the <c>families.{agent,plugin,legacy}</c> schema with
    /// per-family <c>clientId</c> + <c>scope</c>, the top-level v1 compat mirror of the plugin
    /// family, v1 read-compat as <c>families.legacy</c> with first-write upgrade, unknown-field
    /// preservation across a read→write round-trip, version passthrough, and the structured
    /// "store unreadable" state (never crash, never delete, never overwrite until explicit re-auth).
    ///
    /// <para>Raw-document assertions go through the store's internal plaintext seams so they run on
    /// Windows (DPAPI at rest) exactly as on POSIX, and compare <b>parsed values, never bytes</b> —
    /// formatting is codec-specific by design.</para>
    /// </summary>
    public sealed class MachineCredentialStoreV2Tests : IDisposable
    {
        private static readonly DateTimeOffset AgentExpiry = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        private static readonly DateTimeOffset PluginExpiry = new DateTimeOffset(2030, 6, 7, 8, 9, 10, TimeSpan.Zero);
        private static readonly DateTimeOffset LegacyExpiry = new DateTimeOffset(2029, 11, 12, 13, 14, 15, TimeSpan.Zero);

        private readonly string _baseDir;

        public MachineCredentialStoreV2Tests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-store-v2-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        private static MachineCredentials FullV2Credentials() => new MachineCredentials
        {
            ServerTarget = "https://ai-game.dev",
            Subject = "usr_full",
            Families = new MachineCredentialFamilies
            {
                Agent = new MachineCredentialFamily
                {
                    AccessToken = "AGENT-AT",
                    RefreshToken = "AGENT-RT",
                    ExpiresAt = AgentExpiry,
                    ClientId = "unity-mcp-plugin",
                    Scope = "mcp:agent",
                },
                Plugin = new MachineCredentialFamily
                {
                    AccessToken = "PLUGIN-AT",
                    RefreshToken = "PLUGIN-RT",
                    ExpiresAt = PluginExpiry,
                    ClientId = "unity-mcp-plugin",
                    Scope = "mcp:plugin",
                },
                Legacy = new MachineCredentialFamily
                {
                    AccessToken = "LEGACY-AT",
                    RefreshToken = "LEGACY-RT",
                    ExpiresAt = LegacyExpiry,
                },
            },
        };

        private JsonDocument ReadRawDocument()
        {
            var json = NewStore().ReadPlaintextJson();
            json.ShouldNotBeNull();
            return JsonDocument.Parse(json!);
        }

        // ── v2 round-trip ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void WriteV2_ThenRead_RoundTripsAllFamilies()
        {
            var store = NewStore();
            store.Write(FullV2Credentials());

            var read = store.Read();
            read.ShouldNotBeNull();
            read!.Version.ShouldBe(MachineCredentials.CurrentVersion);
            read.ServerTarget.ShouldBe("https://ai-game.dev");
            read.Subject.ShouldBe("usr_full");

            var agent = read.Families!.Agent!;
            agent.AccessToken.ShouldBe("AGENT-AT");
            agent.RefreshToken.ShouldBe("AGENT-RT");
            agent.ExpiresAt.ShouldBe(AgentExpiry);
            agent.ClientId.ShouldBe("unity-mcp-plugin");
            agent.Scope.ShouldBe("mcp:agent");

            var plugin = read.Families.Plugin!;
            plugin.AccessToken.ShouldBe("PLUGIN-AT");
            plugin.RefreshToken.ShouldBe("PLUGIN-RT");
            plugin.ExpiresAt.ShouldBe(PluginExpiry);
            plugin.ClientId.ShouldBe("unity-mcp-plugin");
            plugin.Scope.ShouldBe("mcp:plugin");

            var legacy = read.Families.Legacy!;
            legacy.AccessToken.ShouldBe("LEGACY-AT");
            legacy.RefreshToken.ShouldBe("LEGACY-RT");
            legacy.ExpiresAt.ShouldBe(LegacyExpiry);
            legacy.ClientId.ShouldBeNull(); // unknown by definition
            legacy.Scope.ShouldBeNull();
        }

        // ── v1 compat mirror ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Write_MirrorsPluginFamilyTokens_AtTopLevel()
        {
            NewStore().Write(FullV2Credentials());

            // Model view: the mirror is what pre-v2 model consumers read.
            var read = NewStore().Read()!;
            read.AccessToken.ShouldBe("PLUGIN-AT");
            read.RefreshToken.ShouldBe("PLUGIN-RT");
            read.ExpiresAt.ShouldBe(PluginExpiry);

            // Old-reader simulation on the raw document: a v1 reader keys on the top-level fields.
            using var doc = ReadRawDocument();
            doc.RootElement.GetProperty("version").GetInt32().ShouldBe(2);
            doc.RootElement.GetProperty("accessToken").GetString().ShouldBe("PLUGIN-AT");
            doc.RootElement.GetProperty("refreshToken").GetString().ShouldBe("PLUGIN-RT");
            DateTimeOffset.Parse(doc.RootElement.GetProperty("expiresAt").GetString()!).ShouldBe(PluginExpiry);
        }

        [Fact]
        public void Write_WithoutPluginFamily_MirrorsLegacyFamily()
        {
            var credentials = FullV2Credentials();
            credentials.Families!.Plugin = null;

            NewStore().Write(credentials);

            using var doc = ReadRawDocument();
            doc.RootElement.GetProperty("accessToken").GetString().ShouldBe("LEGACY-AT");
            doc.RootElement.GetProperty("refreshToken").GetString().ShouldBe("LEGACY-RT");
        }

        [Fact]
        public void Write_AgentOnlyDocument_EmitsNoTopLevelTokenMirror()
        {
            var credentials = FullV2Credentials();
            credentials.Families!.Plugin = null;
            credentials.Families.Legacy = null;
            // A stale mirror must never outlive its source: even if a caller left top-level values
            // behind, an agent-only document has no plugin-plane credential to mirror.
            credentials.AccessToken = "STALE";
            credentials.RefreshToken = "STALE";

            NewStore().Write(credentials);

            using var doc = ReadRawDocument();
            doc.RootElement.TryGetProperty("accessToken", out _).ShouldBeFalse();
            doc.RootElement.TryGetProperty("refreshToken", out _).ShouldBeFalse();
            doc.RootElement.TryGetProperty("expiresAt", out _).ShouldBeFalse();
            doc.RootElement.GetProperty("families").TryGetProperty("agent", out _).ShouldBeTrue();
        }

        // ── v1 read-compat + first-write upgrade ─────────────────────────────────────────────────

        private const string V1DocumentWithUnknownField = @"{
            ""version"": 1,
            ""accessToken"": ""V1-AT"",
            ""refreshToken"": ""V1-RT"",
            ""expiresAt"": ""2030-01-02T03:04:05+00:00"",
            ""serverTarget"": ""https://ai-game.dev"",
            ""subject"": ""usr_v1"",
            ""futureField"": { ""nested"": [1, 2, 3] }
        }";

        [Fact]
        public void V1Document_IsReadAsLegacyFamily()
        {
            NewStore().WritePlaintextJson(V1DocumentWithUnknownField);

            var result = NewStore().TryRead();
            result.Status.ShouldBe(MachineCredentialStoreStatus.Ok);

            var credentials = result.Credentials!;
            credentials.Version.ShouldBe(1); // still a v1 document until the first write
            credentials.ServerTarget.ShouldBe("https://ai-game.dev");
            credentials.Subject.ShouldBe("usr_v1");

            var legacy = credentials.Families!.Legacy!;
            legacy.AccessToken.ShouldBe("V1-AT");
            legacy.RefreshToken.ShouldBe("V1-RT");
            legacy.ExpiresAt.ShouldBe(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
            legacy.ClientId.ShouldBeNull();
            legacy.Scope.ShouldBeNull();

            credentials.Families.Agent.ShouldBeNull();
            credentials.Families.Plugin.ShouldBeNull();
        }

        [Fact]
        public void FirstWrite_UpgradesV1DocumentToV2_WithMirror_PreservingUnknownFields()
        {
            var store = NewStore();
            store.WritePlaintextJson(V1DocumentWithUnknownField);

            var credentials = store.Read()!;
            store.Write(credentials); // first write by updated code → v2 + mirror (design 04 §1)

            using var doc = ReadRawDocument();
            doc.RootElement.GetProperty("version").GetInt32().ShouldBe(2);

            var legacy = doc.RootElement.GetProperty("families").GetProperty("legacy");
            legacy.GetProperty("accessToken").GetString().ShouldBe("V1-AT");
            legacy.GetProperty("refreshToken").GetString().ShouldBe("V1-RT");

            // The adopted legacy family is the only plugin-plane credential → it is the mirror.
            doc.RootElement.GetProperty("accessToken").GetString().ShouldBe("V1-AT");
            doc.RootElement.GetProperty("refreshToken").GetString().ShouldBe("V1-RT");

            doc.RootElement.GetProperty("serverTarget").GetString().ShouldBe("https://ai-game.dev");
            doc.RootElement.GetProperty("subject").GetString().ShouldBe("usr_v1");

            // The unknown v1 field survived the upgrade (parsed-value comparison, never bytes).
            var future = doc.RootElement.GetProperty("futureField");
            future.GetProperty("nested").GetArrayLength().ShouldBe(3);
            future.GetProperty("nested")[2].GetInt32().ShouldBe(3);
        }

        [Fact]
        public void Rotate_OnV1Document_RotatesLegacyFamily_AndUpgrades()
        {
            var store = NewStore();
            store.WritePlaintextJson(V1DocumentWithUnknownField);

            store.Rotate("NEW-AT", "NEW-RT", new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero));

            using var doc = ReadRawDocument();
            doc.RootElement.GetProperty("version").GetInt32().ShouldBe(2);

            var legacy = doc.RootElement.GetProperty("families").GetProperty("legacy");
            legacy.GetProperty("accessToken").GetString().ShouldBe("NEW-AT");
            legacy.GetProperty("refreshToken").GetString().ShouldBe("NEW-RT");

            doc.RootElement.GetProperty("accessToken").GetString().ShouldBe("NEW-AT");

            // Identity fields and unknown fields survive the rotation.
            doc.RootElement.GetProperty("subject").GetString().ShouldBe("usr_v1");
            doc.RootElement.TryGetProperty("futureField", out _).ShouldBeTrue();
        }

        [Fact]
        public void Rotate_WithPluginFamily_RotatesPluginFamily_NotAgent()
        {
            var store = NewStore();
            store.Write(FullV2Credentials());

            store.Rotate("ROTATED-AT", "ROTATED-RT");

            var read = store.Read()!;
            read.Families!.Plugin!.AccessToken.ShouldBe("ROTATED-AT");
            read.Families.Plugin.RefreshToken.ShouldBe("ROTATED-RT");
            read.Families.Plugin.ClientId.ShouldBe("unity-mcp-plugin"); // family identity untouched
            read.Families.Plugin.Scope.ShouldBe("mcp:plugin");

            read.Families.Agent!.AccessToken.ShouldBe("AGENT-AT"); // other families untouched
            read.Families.Legacy!.AccessToken.ShouldBe("LEGACY-AT");

            read.AccessToken.ShouldBe("ROTATED-AT"); // mirror follows the plugin family
        }

        // ── Unknown-field preservation (mandated plant: removing [JsonExtensionData] → RED) ──────

        [Fact]
        public void UnknownFields_SurviveReadWriteRoundTrip_AtEveryLevel()
        {
            var store = NewStore();
            store.WritePlaintextJson(@"{
                ""version"": 2,
                ""accessToken"": ""PLUGIN-AT"",
                ""x-top"": { ""nested"": [1, 2, 3], ""flag"": true },
                ""families"": {
                    ""x-unknown-family"": { ""accessToken"": ""FUTURE-AT"" },
                    ""plugin"": {
                        ""accessToken"": ""PLUGIN-AT"",
                        ""refreshToken"": ""PLUGIN-RT"",
                        ""clientId"": ""unity-mcp-plugin"",
                        ""scope"": ""mcp:plugin"",
                        ""x-in-family"": 42
                    }
                }
            }");

            var credentials = store.Read()!;
            store.Write(credentials); // the round-trip that used to drop unknown fields

            using var doc = ReadRawDocument();

            // Top level.
            var top = doc.RootElement.GetProperty("x-top");
            top.GetProperty("nested").GetArrayLength().ShouldBe(3);
            top.GetProperty("nested")[0].GetInt32().ShouldBe(1);
            top.GetProperty("flag").GetBoolean().ShouldBeTrue();

            // families container.
            doc.RootElement.GetProperty("families").GetProperty("x-unknown-family")
                .GetProperty("accessToken").GetString().ShouldBe("FUTURE-AT");

            // Inside a family.
            doc.RootElement.GetProperty("families").GetProperty("plugin")
                .GetProperty("x-in-family").GetInt32().ShouldBe(42);
        }

        [Fact]
        public void VersionPassthrough_NewerDocumentKeepsItsVersionOnWrite()
        {
            var store = NewStore();
            store.WritePlaintextJson(@"{
                ""version"": 3,
                ""accessToken"": ""V3-AT"",
                ""families"": { ""plugin"": { ""accessToken"": ""V3-AT"", ""refreshToken"": ""V3-RT"" } },
                ""v3Only"": ""keep-me""
            }");

            var credentials = store.Read()!;
            store.Write(credentials);

            using var doc = ReadRawDocument();
            doc.RootElement.GetProperty("version").GetInt32().ShouldBe(3); // never downgraded
            doc.RootElement.GetProperty("v3Only").GetString().ShouldBe("keep-me");
        }

        // ── Structured "store unreadable" state (mandated plant: corrupted fixture → RED) ────────

        private void PlantCorruptedStore()
        {
            Directory.CreateDirectory(_baseDir);
            // Not a DPAPI blob (Windows: CryptUnprotectData fails) and not valid JSON (POSIX).
            File.WriteAllBytes(NewStore().CredentialsPath, System.Text.Encoding.UTF8.GetBytes("this-is-not-a-credential-store"));
        }

        [Fact]
        public void TryRead_ReturnsMissing_WhenNoFile()
        {
            var result = NewStore().TryRead();
            result.Status.ShouldBe(MachineCredentialStoreStatus.Missing);
            result.Credentials.ShouldBeNull();
        }

        [Fact]
        public void TryRead_ReturnsUnreadable_OnCorruptedContent_NeverCrashes_NeverDeletes()
        {
            PlantCorruptedStore();
            var store = NewStore();
            var originalBytes = File.ReadAllBytes(store.CredentialsPath);

            MachineCredentialReadResult result = default!;
            Should.NotThrow(() => result = store.TryRead()); // never crash (design 04 §1)

            result.Status.ShouldBe(MachineCredentialStoreStatus.Unreadable);
            result.Credentials.ShouldBeNull();
            result.Error.ShouldNotBeNullOrWhiteSpace();

            // Never delete, never overwrite: the store is preserved byte-for-byte for the user.
            File.Exists(store.CredentialsPath).ShouldBeTrue();
            File.ReadAllBytes(store.CredentialsPath).ShouldBe(originalBytes);
        }

        [Fact]
        public void TryRead_ReturnsUnreadable_OnEmptyFile()
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllBytes(NewStore().CredentialsPath, Array.Empty<byte>());

            NewStore().TryRead().Status.ShouldBe(MachineCredentialStoreStatus.Unreadable);
        }

        [Fact]
        public void TryRead_ReturnsUnreadable_OnValidBlobWithCorruptedJson()
        {
            // On Windows this is a VALID DPAPI blob whose payload is not JSON — the failure moves
            // from the unprotect stage to the decode stage; the state must be the same.
            NewStore().WritePlaintextJson("{ this is not json");

            NewStore().TryRead().Status.ShouldBe(MachineCredentialStoreStatus.Unreadable);
        }

        [Fact]
        public void Read_ReturnsNull_ForUnreadableStore_WithoutThrowing()
        {
            PlantCorruptedStore();

            MachineCredentials? read = null;
            Should.NotThrow(() => read = NewStore().Read());
            read.ShouldBeNull();
        }

        [Fact]
        public void Exists_IsTrue_ForUnreadableStore_SoItIsNeverASignedInSignal()
        {
            PlantCorruptedStore();
            var store = NewStore();

            store.Exists.ShouldBeTrue(); // the file exists...
            store.TryRead().Status.ShouldBe(MachineCredentialStoreStatus.Unreadable); // ...but is no credential
        }

        [Fact]
        public void Rotate_OnUnreadableStore_Throws_AndPreservesTheFile()
        {
            PlantCorruptedStore();
            var store = NewStore();
            var originalBytes = File.ReadAllBytes(store.CredentialsPath);

            // An automated rotation must never overwrite a store the user has not explicitly
            // re-authorized to replace (design 04 §1).
            Should.Throw<InvalidOperationException>(() => store.Rotate("NEW-AT", "NEW-RT"));

            File.ReadAllBytes(store.CredentialsPath).ShouldBe(originalBytes);
        }

        [Fact]
        public void Write_AfterExplicitReauth_ReplacesUnreadableStore()
        {
            PlantCorruptedStore();
            var store = NewStore();

            // Write is the explicit re-auth surface (login/adopt) — it may replace the store.
            store.Write(FullV2Credentials());

            var result = store.TryRead();
            result.Status.ShouldBe(MachineCredentialStoreStatus.Ok);
            result.Credentials!.Families!.Plugin!.AccessToken.ShouldBe("PLUGIN-AT");
        }
    }
}
