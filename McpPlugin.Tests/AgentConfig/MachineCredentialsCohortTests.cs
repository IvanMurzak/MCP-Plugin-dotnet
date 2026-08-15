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
    /// One case per <c>06-migration-rollout.md</c> cohort row that has a store FILE-SHAPE (v1-only
    /// reader, mixed writer, legacy family) — O4 REVISED: "no cohort regresses below status quo at
    /// any intermediate step", each verified here against the committed
    /// <c>MachineCredentials.GoldenVectors.json</c> shared with the TS twin (task x1). The C# twin of
    /// cli-core's <c>test/machine-credentials-cohort.test.ts</c>.
    /// </summary>
    public sealed class MachineCredentialsCohortTests : IDisposable
    {
        private const string GoldenFileName = "MachineCredentials.GoldenVectors.json";
        private static string GoldenFilePath => Path.Combine(AppContext.BaseDirectory, GoldenFileName);
        private static JsonDocument LoadVectors() => JsonDocument.Parse(File.ReadAllText(GoldenFilePath));

        private readonly string _baseDir;

        public MachineCredentialsCohortTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-cred-cohort-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        /// <summary>
        /// Cohort row "Credential minted by own plugin (v1 file)": an OLD (v1-only) reader keys only
        /// on the top-level fields; an UPDATED reader sees the identical values via
        /// <c>families.legacy</c>. Neither reader is worse off than before the rollout — status quo,
        /// not regressed (06 row 1 / O4 REVISED).
        /// </summary>
        [Fact]
        public void Cohort_V1OnlyReader_AndUpdatedReader_SeeIdenticalTokenValues()
        {
            using var vectors = LoadVectors();
            var v1 = vectors.RootElement.GetProperty("v1Document");
            var store = NewStore();
            store.WritePlaintextJson(v1.GetRawText());

            // The OLD reader simulation: read the raw top-level fields only (no families concept).
            using var rawDoc = JsonDocument.Parse(store.ReadPlaintextJson()!);
            var oldReaderAccessToken = rawDoc.RootElement.GetProperty("accessToken").GetString();
            var oldReaderRefreshToken = rawDoc.RootElement.GetProperty("refreshToken").GetString();

            // The UPDATED reader: families.legacy view.
            var updatedRead = store.Read()!;
            var legacy = updatedRead.Families!.Legacy!;

            legacy.AccessToken.ShouldBe(oldReaderAccessToken);
            legacy.RefreshToken.ShouldBe(oldReaderRefreshToken);
            legacy.AccessToken.ShouldBe(v1.GetProperty("accessToken").GetString());
        }

        /// <summary>
        /// Cohort row "Mixed versions (updated writer + old reader)": an old C# <c>Rotate()</c> that
        /// predates the families schema rewrites the v2 file as pure v1 (its codec drops
        /// <c>families.*</c>) — the machine "degrades to v1 state until the next updated-component
        /// touch re-adopts" (06 row 4, F11.1). This proves the heal: after the degraded document is
        /// planted, the next updated read+write reproduces the golden
        /// <c>reAdoptedOnNextUpdatedWrite</c> shape exactly — no token lost across the degrade cycle.
        /// </summary>
        [Fact]
        public void Cohort_MixedVersions_OldWriterCollapseToV1_HealsOnNextUpdatedWrite()
        {
            using var vectors = LoadVectors();
            var cohort = vectors.RootElement.GetProperty("cohortMixedVersionsOldWriterCollapse");
            var collapsed = cohort.GetProperty("document");
            var expectedHealed = cohort.GetProperty("reAdoptedOnNextUpdatedWrite");

            var store = NewStore();
            // Plant the collapsed pure-v1 on-disk shape, exactly as an old writer would have left it.
            store.WritePlaintextJson(collapsed.GetRawText());
            collapsed.GetProperty("version").GetInt32().ShouldBe(1); // sanity: the vector really is v1-shaped

            // The next updated-component touch: read (adopts to families.legacy in-memory), write
            // (persists v2 + mirror) — 04 §1 "first write by updated code upgrades".
            var credentials = store.Read()!;
            store.Write(credentials);

            using var raw = JsonDocument.Parse(store.ReadPlaintextJson()!);
            raw.RootElement.GetProperty("version").GetInt32().ShouldBe(expectedHealed.GetProperty("version").GetInt32());
            raw.RootElement.GetProperty("accessToken").GetString().ShouldBe(expectedHealed.GetProperty("accessToken").GetString());
            raw.RootElement.GetProperty("refreshToken").GetString().ShouldBe(expectedHealed.GetProperty("refreshToken").GetString());
            raw.RootElement.GetProperty("serverTarget").GetString().ShouldBe(expectedHealed.GetProperty("serverTarget").GetString());
            raw.RootElement.GetProperty("subject").GetString().ShouldBe(expectedHealed.GetProperty("subject").GetString());

            var legacy = raw.RootElement.GetProperty("families").GetProperty("legacy");
            var expectedLegacy = expectedHealed.GetProperty("families").GetProperty("legacy");
            legacy.GetProperty("accessToken").GetString().ShouldBe(expectedLegacy.GetProperty("accessToken").GetString());
            legacy.GetProperty("refreshToken").GetString().ShouldBe(expectedLegacy.GetProperty("refreshToken").GetString());

            // Provenance was genuinely lost by the collapse (clientId/scope), same as any legacy family.
            var readBack = store.Read()!;
            readBack.Families!.Legacy!.ClientId.ShouldBeNull();
            readBack.Families.Legacy.Scope.ShouldBeNull();
        }
    }
}
