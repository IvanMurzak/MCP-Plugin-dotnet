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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Verifies <see cref="ProjectIdentity"/> against the committed cross-language golden-vector file
    /// and against the shipped Unity <c>GeneratePortFromDirectory</c> algorithm (port compatibility
    /// gate). Also covers the C# <c>ToLowerInvariant</c> vs JS <c>toLowerCase()</c> Unicode divergence
    /// and the user port-override precedence.
    /// </summary>
    public class ProjectIdentityGoldenVectorTests
    {
        private const string GoldenFileName = "ProjectIdentity.GoldenVectors.json";

        private static string GoldenFilePath => Path.Combine(AppContext.BaseDirectory, GoldenFileName);

        public static IEnumerable<object[]> GoldenVectors()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFilePath));
            foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                yield return new object[]
                {
                    v.GetProperty("path").GetString()!,
                    v.GetProperty("pin").GetString()!,
                    v.GetProperty("port").GetInt32(),
                };
            }
        }

        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void ProjectIdentity_ReproducesEveryGoldenVector(string path, string expectedPin, int expectedPort)
        {
            ProjectIdentity.DerivePin(path).ShouldBe(expectedPin);
            ProjectIdentity.DerivePort(path).ShouldBe(expectedPort);

            var id = ProjectIdentity.Derive(path);
            id.Pin.ShouldBe(expectedPin);
            id.Port.ShouldBe(expectedPort);
            id.PortIsOverridden.ShouldBeFalse();
        }

        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void EveryGoldenVector_HasValidPinAndPortShape(string path, string expectedPin, int expectedPort)
        {
            _ = path;
            expectedPin.Length.ShouldBe(ProjectIdentity.PinLength);
            foreach (var c in expectedPin)
                ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).ShouldBeTrue($"pin '{expectedPin}' must be lowercase hex");

            expectedPort.ShouldBeInRange(ProjectIdentity.MinPort, ProjectIdentity.MaxPort);
        }

        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void DerivedPort_ByteMatchesUnityGeneratePortFromDirectory_ForExistingProjectPaths(
            string path, string expectedPin, int expectedPort)
        {
            _ = expectedPin;
            _ = expectedPort;

            // The port-compatibility gate: for an existing project path (as Unity reports it via
            // Environment.CurrentDirectory — no trailing separator), our derived port must equal
            // the shipped Unity algorithm byte-for-byte. Trailing-separator variants are not paths
            // Unity would ever hash (its CurrentDirectory never has one), so they are covered by the
            // trimming test below instead.
            if (HasTrailingSeparator(path))
                return;

            ProjectIdentity.DerivePort(path).ShouldBe(UnityGeneratePortFromDirectory(path));
        }

        [Fact]
        public void TrailingSeparator_IsTrimmed_SameIdentityAsWithout()
        {
            ProjectIdentity.DerivePin("/home/user/my-game/").ShouldBe(ProjectIdentity.DerivePin("/home/user/my-game"));
            ProjectIdentity.DerivePort("/home/user/my-game/").ShouldBe(ProjectIdentity.DerivePort("/home/user/my-game"));

            var backslash = "C:" + Bs + "Games" + Bs + "proj";
            ProjectIdentity.DerivePort(backslash + Bs).ShouldBe(ProjectIdentity.DerivePort(backslash));
        }

        [Fact]
        public void Separators_AreNotNormalized_ForwardAndBackslashDiffer()
        {
            var forward = "C:/Users/user/my-game";
            var backslash = "C:" + Bs + "Users" + Bs + "user" + Bs + "my-game";
            ProjectIdentity.DerivePort(forward).ShouldNotBe(ProjectIdentity.DerivePort(backslash));
        }

        [Fact]
        public void CaseFolding_IsApplied()
        {
            ProjectIdentity.DerivePin("/HOME/User/My-Game").ShouldBe(ProjectIdentity.DerivePin("/home/user/my-game"));
        }

        [Fact]
        public void PortOverride_AlwaysWins_PinUnchanged()
        {
            const string path = "/home/user/my-game";
            var overridden = ProjectIdentity.Derive(path, portOverride: 12345);

            overridden.Port.ShouldBe(12345);
            overridden.PortIsOverridden.ShouldBeTrue();
            overridden.Pin.ShouldBe(ProjectIdentity.DerivePin(path)); // pin is never affected by the override
        }

        [Fact]
        public void Derive_WithMarker_UsesMarkerPortOverride()
        {
            const string path = "/home/user/my-game";

            var withOverride = ProjectIdentity.Derive(path, new ProjectMarker { PortOverride = 26000 });
            withOverride.Port.ShouldBe(26000);
            withOverride.PortIsOverridden.ShouldBeTrue();

            var noOverride = ProjectIdentity.Derive(path, new ProjectMarker { ServerTarget = "https://ai-game.dev" });
            noOverride.Port.ShouldBe(ProjectIdentity.DerivePort(path));
            noOverride.PortIsOverridden.ShouldBeFalse();

            var nullMarker = ProjectIdentity.Derive(path, (ProjectMarker?)null);
            nullMarker.Port.ShouldBe(ProjectIdentity.DerivePort(path));
        }

        [Fact]
        public void UnicodeDivergence_CanonicalIsCSharp_AndDiffersFromNaiveJs()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFilePath));
            foreach (var c in doc.RootElement.GetProperty("unicodeDivergence").GetProperty("cases").EnumerateArray())
            {
                var path = c.GetProperty("path").GetString()!;
                var canonical = c.GetProperty("canonical");
                var jsNaive = c.GetProperty("jsNaiveToLowerCase");

                // The C# reference IS the canonical value.
                ProjectIdentity.DerivePin(path).ShouldBe(canonical.GetProperty("pin").GetString());
                ProjectIdentity.DerivePort(path).ShouldBe(canonical.GetProperty("port").GetInt32());

                // ...and it genuinely differs from what a naive JS toLowerCase() port would produce,
                // proving the divergence is real and must be special-cased by the TS port.
                ProjectIdentity.DerivePort(path).ShouldNotBe(jsNaive.GetProperty("port").GetInt32());
                ProjectIdentity.DerivePin(path).ShouldNotBe(jsNaive.GetProperty("pin").GetString());
            }
        }

        [Fact]
        public void Derive_NullPath_Throws()
        {
            Should.Throw<ArgumentNullException>(() => ProjectIdentity.Derive(null!));
        }

        private static string Bs => ((char)92).ToString();

        private static bool HasTrailingSeparator(string path)
            => path.Length > 0 && (path[path.Length - 1] == '/' || path[path.Length - 1] == '\\');

        /// <summary>
        /// Verbatim copy of the shipped Unity <c>UnityMcpPlugin.GeneratePortFromDirectory()</c> body
        /// (Runtime/UnityMcpPlugin.cs), used as the external truth for the byte-match gate. Kept
        /// deliberately independent of <see cref="ProjectIdentity"/> so the gate would catch any drift.
        /// </summary>
        private static int UnityGeneratePortFromDirectory(string dir)
        {
            const int MinPort = 20000;
            const int MaxPort = 29999;
            const int PortRange = MaxPort - MinPort + 1;

            var currentDir = dir.ToLowerInvariant();
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(currentDir));
            var hash = (uint)BitConverter.ToInt32(hashBytes, 0);
            return MinPort + (int)(hash % PortRange);
        }
    }
}
