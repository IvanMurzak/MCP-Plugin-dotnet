/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection.Credentials
{
    /// <summary>
    /// The shared sidecar <c>plugin-dead-family.json</c> is read and written by this package AND by
    /// AI-Game-Dev-CLI-Core (<c>dead-family-memo.ts</c>). Both are pinned to the same committed
    /// <c>DeadFamilyMemo.GoldenVectors.json</c> (generated with Node's <c>crypto</c> +
    /// <c>JSON.stringify</c>, vendored byte-identical into cli-core): writes compare RAW BYTES,
    /// reads compare the verdict.
    /// </summary>
    public sealed class DeadFamilyMemoGoldenVectorTests : IDisposable
    {
        const string GoldenFileName = "DeadFamilyMemo.GoldenVectors.json";

        readonly string _baseDir;

        public DeadFamilyMemoGoldenVectorTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-deadmemo-golden-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, recursive: true);
        }

        static JsonDocument LoadVectors()
            => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, GoldenFileName)));

        public static IEnumerable<object[]> WriteVectorNames() => VectorNames("writes");
        public static IEnumerable<object[]> ReadVectorNames() => VectorNames("reads");

        static IEnumerable<object[]> VectorNames(string section)
        {
            using var vectors = LoadVectors();
            foreach (var vector in vectors.RootElement.GetProperty(section).EnumerateArray())
                yield return new object[] { vector.GetProperty("name").GetString()! };
        }

        static JsonElement Vector(JsonDocument vectors, string section, string name)
        {
            foreach (var vector in vectors.RootElement.GetProperty(section).EnumerateArray())
            {
                if (vector.GetProperty("name").GetString() == name)
                    return vector;
            }
            throw new InvalidOperationException("no vector " + section + "/" + name);
        }

        [Theory]
        [MemberData(nameof(WriteVectorNames))]
        public void Write_ProducesTheGoldenBytes(string name)
        {
            using var vectors = LoadVectors();
            var vector = Vector(vectors, "writes", name);
            var refreshToken = vector.GetProperty("refreshToken").GetString()!;
            var reason = vector.GetProperty("reason").GetString()!;
            var at = DateTimeOffset.FromUnixTimeMilliseconds(vector.GetProperty("declaredDeadAtMs").GetInt64());

            PersistedDeadFamilyMemo.Hash(refreshToken).ShouldBe(vector.GetProperty("refreshTokenSha256").GetString());

            new PersistedDeadFamilyMemo(_baseDir, logger: null).Write(refreshToken, reason, at);

            var expected = new UTF8Encoding(false).GetBytes(vector.GetProperty("expectedFile").GetString()!);
            File.ReadAllBytes(Path.Combine(_baseDir, PersistedDeadFamilyMemo.FileName)).ShouldBe(expected);
        }

        [Theory]
        [MemberData(nameof(ReadVectorNames))]
        public void Read_ReachesTheGoldenVerdict(string name)
        {
            using var vectors = LoadVectors();
            var vector = Vector(vectors, "reads", name);
            File.WriteAllText(Path.Combine(_baseDir, PersistedDeadFamilyMemo.FileName), vector.GetProperty("file").GetString()!, new UTF8Encoding(false));

            var dead = new PersistedDeadFamilyMemo(_baseDir, logger: null)
                .Matches(vector.GetProperty("refreshToken").GetString()!, out var reason);

            var expectedDead = vector.GetProperty("expected").GetString() == "dead";
            dead.ShouldBe(expectedDead);
            if (expectedDead)
                reason.ShouldBe(vector.GetProperty("expectedReason").GetString());
        }
    }
}
