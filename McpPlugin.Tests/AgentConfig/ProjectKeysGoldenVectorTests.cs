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
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The C# side of the project-key cache parity contract (project-keys contract §6): every vector in
    /// <c>project-keys.golden.json</c> — issuer-origin normalisation, entry naming, pin validation, reading the
    /// canonical plain-JSON document, and the read-modify-write merge that must preserve unknown fields/entries —
    /// is asserted against the REAL <see cref="ProjectKeyStore"/>. cli-core vendors the same file byte-identical,
    /// so the two implementations cannot drift. Comparisons are PARSED values (the writers indent differently).
    /// </summary>
    public sealed class ProjectKeysGoldenVectorTests : IDisposable
    {
        private const string GoldenFileName = "project-keys.golden.json";
        private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "agd-pk-golden-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private static JsonObject Vectors() =>
            (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GoldenFileName)))!;

        private ProjectKeyStore NewStore() => new ProjectKeyStore(_baseDir);

        [Fact]
        public void IssuerOrigins_NormaliseExactlyAsTheVectorsSay()
        {
            var cases = Vectors()["issuerOrigins"]!.AsArray();
            cases.Count.ShouldBeGreaterThan(5);
            foreach (var c in cases)
                ProjectKeyStore.NormalizeIssuerOrigin((string)c!["input"]!).ShouldBe((string)c["origin"]!, (string)c["input"]!);
        }

        [Fact]
        public void InvalidIssuers_AreRejected()
        {
            foreach (var bad in Vectors()["invalidIssuers"]!.AsArray())
                Should.Throw<ArgumentException>(() => ProjectKeyStore.NormalizeIssuerOrigin((string)bad!), (string)bad!);
        }

        [Fact]
        public void EntryNames_AreIssuerOriginHashPin()
        {
            foreach (var c in Vectors()["entryNames"]!.AsArray())
                ProjectKeyStore.EntryName((string)c!["issuer"]!, (string)c["pin"]!).ShouldBe((string)c["name"]!);
        }

        [Fact]
        public void InvalidPins_AreRejected()
        {
            foreach (var bad in Vectors()["invalidPins"]!.AsArray())
                Should.Throw<ArgumentException>(() => ProjectKeyStore.NormalizePin((string)bad!), (string)bad!);
        }

        [Fact]
        public void CanonicalDocument_ReadByTheStore_MatchesEveryLookup()
        {
            var v = Vectors();
            // Plant the canonical plain JSON on disk as the OTHER implementation would write it (through the
            // real protection seam, so on Windows this is a genuine DPAPI round-trip).
            NewStore().WritePlaintextJson(v["document"]!.ToJsonString());

            var lookups = v["lookups"]!.AsArray();
            lookups.Count.ShouldBe(4);
            foreach (var l in lookups)
            {
                var entry = NewStore().Get((string)l!["issuer"]!, (string)l["pin"]!);
                var expectKey = (string?)l["expectKey"];
                if (expectKey == null)
                {
                    entry.ShouldBeNull();
                    continue;
                }
                entry.ShouldNotBeNull();
                entry!.Key.ShouldBe(expectKey);
                entry.Sub.ShouldBe((string)l["expectSub"]!);
                entry.KeyId.ShouldBe((string)l["expectKeyId"]!);
            }
        }

        [Fact]
        public void Put_OverwritesOneEntry_AndPreservesEverythingElse()
        {
            var v = Vectors();
            NewStore().WritePlaintextJson(v["document"]!.ToJsonString());

            NewStore().Put(ToEntry(v["put"]!["entry"]!.AsObject()));

            AssertJsonEqual(v["put"]!["after"]!, JsonNode.Parse(NewStore().ReadPlaintextJson()!)!);
        }

        [Fact]
        public void Put_IntoMissingFile_WritesTheCanonicalShape()
        {
            var v = Vectors();
            NewStore().Put(ToEntry(v["putIntoEmpty"]!["entry"]!.AsObject()));

            AssertJsonEqual(v["putIntoEmpty"]!["after"]!, JsonNode.Parse(NewStore().ReadPlaintextJson()!)!);
        }

        private static ProjectKeyEntry ToEntry(JsonObject o) => new ProjectKeyEntry
        {
            Key = (string)o["key"]!,
            KeyId = (string?)o["keyId"],
            Pin = (string)o["pin"]!,
            Issuer = (string)o["issuer"]!,
            Sub = (string?)o["sub"],
            Engine = (string?)o["engine"],
            CreatedAt = (string?)o["createdAt"],
        };

        private static void AssertJsonEqual(JsonNode expected, JsonNode actual)
        {
            JsonNode.DeepEquals(expected, actual).ShouldBeTrue(
                "expected:\n" + expected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
                "\nactual:\n" + actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
