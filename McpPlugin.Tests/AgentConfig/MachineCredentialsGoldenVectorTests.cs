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
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The C# twin of cli-core's <c>test/machine-credentials-v2.test.ts</c> "golden vector" describe
    /// blocks (unified-machine-auth 04 §1, task x1): both implementations are asserted against the
    /// SAME committed <c>MachineCredentials.GoldenVectors.json</c> (byte-identical copy — see
    /// <c>McpPlugin.Tests.csproj</c>), so the store contract can never drift silently between the two
    /// languages. Comparisons are PARSED VALUES, never raw bytes (the C# codec writes indented JSON,
    /// the TS codec 2-space — indent asymmetry is deliberate, 04 §5).
    ///
    /// <para>Covers: v2 serialization both directions, v1→v2 adoption, the v2→v1-mirror
    /// old-reader-simulation (a mandated plant — drop the mirror step and this file's
    /// <see cref="V2Document_OldReaderSimulation_SeesExactlyThePluginFamilyAtTopLevel"/> goes RED),
    /// per-family <c>clientId</c>/<c>scope</c> fidelity, and <c>subject</c> handling.</para>
    /// </summary>
    public sealed class MachineCredentialsGoldenVectorTests : IDisposable
    {
        private const string GoldenFileName = "MachineCredentials.GoldenVectors.json";
        private static string GoldenFilePath => Path.Combine(AppContext.BaseDirectory, GoldenFileName);
        private static JsonDocument LoadVectors() => JsonDocument.Parse(File.ReadAllText(GoldenFilePath));

        private readonly string _baseDir;

        public MachineCredentialsGoldenVectorTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-cred-golden-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        private static void AssertFamily(JsonElement expectedFamily, MachineCredentialFamily? actual, string label)
        {
            actual.ShouldNotBeNull(label + " family must be present");
            actual!.AccessToken.ShouldBe(expectedFamily.GetProperty("accessToken").GetString());
            actual.RefreshToken.ShouldBe(expectedFamily.GetProperty("refreshToken").GetString());
            if (expectedFamily.TryGetProperty("expiresAt", out var expiresAt))
                actual.ExpiresAt.ShouldBe(DateTimeOffset.Parse(expiresAt.GetString()!));
            if (expectedFamily.TryGetProperty("clientId", out var clientId))
                actual.ClientId.ShouldBe(clientId.GetString());
            if (expectedFamily.TryGetProperty("scope", out var scope))
                actual.Scope.ShouldBe(scope.GetString());
        }

        // ── v2 round-trip (golden vector) ────────────────────────────────────────────────────────

        [Fact]
        public void V2Document_WrittenThenRead_MatchesGoldenVectorValueForValue()
        {
            using var vectors = LoadVectors();
            var expected = vectors.RootElement.GetProperty("v2Document");

            // Plant the TS-authored document verbatim on disk (as if TS had written it) — proves the
            // C# codec READS what the TS codec's shape actually is, not just what C# itself wrote.
            NewStore().WritePlaintextJson(expected.GetRawText());

            var read = NewStore().Read();
            read.ShouldNotBeNull();
            read!.Version.ShouldBe(expected.GetProperty("version").GetInt32());
            read.ServerTarget.ShouldBe(expected.GetProperty("serverTarget").GetString());
            read.Subject.ShouldBe(expected.GetProperty("subject").GetString());
            read.AccessToken.ShouldBe(expected.GetProperty("accessToken").GetString());
            read.RefreshToken.ShouldBe(expected.GetProperty("refreshToken").GetString());

            var families = expected.GetProperty("families");
            AssertFamily(families.GetProperty("agent"), read.Families?.Agent, "agent");
            AssertFamily(families.GetProperty("plugin"), read.Families?.Plugin, "plugin");

            // Family-distinct vector values: the agent and plugin tokens must never be confused.
            read.Families!.Agent!.AccessToken.ShouldNotBe(read.Families.Plugin!.AccessToken);
        }

        [Fact]
        public void V2Document_OldReaderSimulation_SeesExactlyThePluginFamilyAtTopLevel()
        {
            // Write via the REAL object API (built from the vector) so the mirror-emission code path
            // actually runs — this is the assertion a mandated "drop the mirror" plant must redden.
            using var vectors = LoadVectors();
            var expected = vectors.RootElement.GetProperty("v2Document");
            var credentials = JsonSerializer.Deserialize<MachineCredentials>(expected.GetRawText());
            credentials.ShouldNotBeNull();

            // Strip the pre-baked top-level triple: the vector's top level already matches
            // families.plugin (it doubles as the round-trip fixture above), so leaving it in place
            // would make this assertion pass even if NormalizeForWrite() stopped re-stamping the
            // mirror (verified locally with a mandated mirror-drop plant). Clearing it first means
            // the ONLY source of the on-disk top-level values is the real mirror step.
            credentials!.AccessToken = null;
            credentials.RefreshToken = null;
            credentials.ExpiresAt = null;

            NewStore().Write(credentials);

            var rawJson = NewStore().ReadPlaintextJson();
            rawJson.ShouldNotBeNull();
            using var raw = JsonDocument.Parse(rawJson!);

            var pluginExpected = expected.GetProperty("families").GetProperty("plugin");
            var agentExpected = expected.GetProperty("families").GetProperty("agent");

            // An old v1-only reader keys ONLY on these top-level fields — never on `families`.
            raw.RootElement.GetProperty("accessToken").GetString().ShouldBe(pluginExpected.GetProperty("accessToken").GetString());
            raw.RootElement.GetProperty("refreshToken").GetString().ShouldBe(pluginExpected.GetProperty("refreshToken").GetString());
            DateTimeOffset.Parse(raw.RootElement.GetProperty("expiresAt").GetString()!)
                .ShouldBe(DateTimeOffset.Parse(pluginExpected.GetProperty("expiresAt").GetString()!));

            // The mirror must be the PLUGIN family, never the agent family.
            raw.RootElement.GetProperty("accessToken").GetString().ShouldNotBe(agentExpected.GetProperty("accessToken").GetString());
        }

        // ── Per-family clientId fidelity (review B1, x1 fix round 1) ────────────────────────────────
        //
        // families.agent.clientId and families.plugin.clientId used to be identical
        // ("unity-mcp-plugin" on both), so ANY assertion that only checks "does the family have A
        // clientId" — or that round-trips vectors.v2Document through Write()/Read() and compares the
        // result back to the SAME parsed vector (as V2Document_WrittenThenRead... above does) —
        // passes under a cross-family clientId swap, because both the written input and the expected
        // value move together (verified locally: swapping the vector's two clientId strings left
        // every existing vector-driven test green, TS and C# alike — the swap is invisible to a
        // write-then-read echo by construction, since clientId has no independent derivation to check
        // against, unlike ProjectIdentity.DerivePin).
        //
        // The two literals below are therefore deliberately HARDCODED, redundantly with the vector,
        // so a future accidental re-symmetrization (or an authoring mistake that swaps which family
        // gets which id) reddens THIS file even though it would not redden the round-trip test above.

        private const string ExpectedAgentClientId = "agd-app-8f3a1c2e";
        private const string ExpectedPluginClientId = "unity-mcp-plugin";

        [Fact]
        public void Vector_PinsTheExpectedLiteralClientId_UnderEachFamily_SanityFailsLoudlyIfTheVectorDrifts()
        {
            using var vectors = LoadVectors();
            var families = vectors.RootElement.GetProperty("v2Document").GetProperty("families");
            families.GetProperty("agent").GetProperty("clientId").GetString().ShouldBe(ExpectedAgentClientId);
            families.GetProperty("plugin").GetProperty("clientId").GetString().ShouldBe(ExpectedPluginClientId);
            ExpectedAgentClientId.ShouldNotBe(ExpectedPluginClientId);
        }

        [Fact]
        public void PerFamilyWrite_KeepsEachFamilysClientId_UnderItsOwnFamilyKey_NeverTheOthers()
        {
            // Exercises the real object-model write path (not a bulk write(vectors.v2Document)) so a
            // hypothetical per-family assignment bug (family A's data landing under family B's key)
            // is also caught, not just a vector-authoring mistake.
            var credentials = new MachineCredentials
            {
                Families = new MachineCredentialFamilies
                {
                    Agent = new MachineCredentialFamily { AccessToken = "a", RefreshToken = "r", ClientId = ExpectedAgentClientId, Scope = "mcp:agent" },
                    Plugin = new MachineCredentialFamily { AccessToken = "a2", RefreshToken = "r2", ClientId = ExpectedPluginClientId, Scope = "mcp:plugin" },
                },
            };
            NewStore().Write(credentials);

            var read = NewStore().Read();
            read.ShouldNotBeNull();
            read!.Families!.Agent!.ClientId.ShouldBe(ExpectedAgentClientId);
            read.Families.Plugin!.ClientId.ShouldBe(ExpectedPluginClientId);
            // The discriminating check: NOT merely "the two differ" (a swap keeps them differing from
            // each other too) — each is pinned to ITS OWN hardcoded literal, so a swap mismatches here.
            read.Families.Agent.ClientId.ShouldNotBe(ExpectedPluginClientId);
            read.Families.Plugin.ClientId.ShouldNotBe(ExpectedAgentClientId);
        }

        // ── v1 read-compat and adoption (04 §1 / F11.1, golden vector) ──────────────────────────────

        [Fact]
        public void V1Document_ReadAsLegacyFamily_IsAViewOnly_FileStaysUntouched()
        {
            using var vectors = LoadVectors();
            var v1 = vectors.RootElement.GetProperty("v1Document");
            var store = NewStore();
            store.WritePlaintextJson(v1.GetRawText());
            var before = File.ReadAllBytes(store.CredentialsPath);

            var read = store.Read();
            read.ShouldNotBeNull();
            AssertFamily(v1, read!.Families?.Legacy, "legacy");
            read.Families!.Legacy!.ClientId.ShouldBeNull(); // unknown by definition (04 §1)
            read.Families.Legacy.Scope.ShouldBeNull();
            read.Families.Agent.ShouldBeNull();
            read.Families.Plugin.ShouldBeNull();

            // Reading is a VIEW — the v1 file itself is untouched.
            File.ReadAllBytes(store.CredentialsPath).ShouldBe(before);
        }

        [Fact]
        public void V1Document_UpgradedOnFirstWrite_MatchesGoldenAdoptionResultExactly()
        {
            using var vectors = LoadVectors();
            var v1 = vectors.RootElement.GetProperty("v1Document");
            var expectedAdopted = vectors.RootElement.GetProperty("v1DocumentAdoptedToV2");

            var store = NewStore();
            store.WritePlaintextJson(v1.GetRawText());

            var credentials = store.Read(); // v1 read-compat -> families.legacy (in-memory only)
            store.Write(credentials!); // first write by updated code -> v2 + mirror (04 §1 / F11.1)

            var rawJson = store.ReadPlaintextJson();
            using var raw = JsonDocument.Parse(rawJson!);

            raw.RootElement.GetProperty("version").GetInt32().ShouldBe(expectedAdopted.GetProperty("version").GetInt32());
            raw.RootElement.GetProperty("accessToken").GetString().ShouldBe(expectedAdopted.GetProperty("accessToken").GetString());
            raw.RootElement.GetProperty("refreshToken").GetString().ShouldBe(expectedAdopted.GetProperty("refreshToken").GetString());
            raw.RootElement.GetProperty("serverTarget").GetString().ShouldBe(expectedAdopted.GetProperty("serverTarget").GetString());
            raw.RootElement.GetProperty("subject").GetString().ShouldBe(expectedAdopted.GetProperty("subject").GetString());
            raw.RootElement.GetProperty("futureUnknownField").GetString()
                .ShouldBe(expectedAdopted.GetProperty("futureUnknownField").GetString());

            var legacy = raw.RootElement.GetProperty("families").GetProperty("legacy");
            var expectedLegacy = expectedAdopted.GetProperty("families").GetProperty("legacy");
            legacy.GetProperty("accessToken").GetString().ShouldBe(expectedLegacy.GetProperty("accessToken").GetString());
            legacy.GetProperty("refreshToken").GetString().ShouldBe(expectedLegacy.GetProperty("refreshToken").GetString());
        }
    }
}
