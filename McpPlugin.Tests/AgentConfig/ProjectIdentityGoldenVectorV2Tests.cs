/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Verifies <see cref="ProjectIdentity"/> v2 (auth-fixes T3 / defect B5) against the committed
    /// cross-language golden-vector file <c>ProjectIdentity.GoldenVectors.v2.json</c>. v2 differs from
    /// v1 by converting <c>'\'</c> to <c>'/'</c> before hashing, so a Windows project root reported
    /// with backslashes and the same root reported with forward slashes hash IDENTICALLY. The v1
    /// vectors + <see cref="ProjectIdentityGoldenVectorTests"/> are kept untouched for the legacy hash.
    /// Named with the <c>ProjectIdentityGoldenVector</c> prefix so the CI <c>golden-vector-parity</c>
    /// job (filter <c>FullyQualifiedName~ProjectIdentityGoldenVector</c>) picks it up.
    /// </summary>
    public class ProjectIdentityGoldenVectorV2Tests
    {
        private const string GoldenFileName = "ProjectIdentity.GoldenVectors.v2.json";

        private static string GoldenFilePath => Path.Combine(System.AppContext.BaseDirectory, GoldenFileName);

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
        public void ProjectIdentityV2_ReproducesEveryGoldenVector(string path, string expectedPin, int expectedPort)
        {
            ProjectIdentity.DerivePinV2(path).ShouldBe(expectedPin);
            ProjectIdentity.DerivePortV2(path).ShouldBe(expectedPort);
        }

        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void EveryV2GoldenVector_HasValidPinAndPortShape(string path, string expectedPin, int expectedPort)
        {
            _ = path;
            expectedPin.Length.ShouldBe(ProjectIdentity.PinLength);
            foreach (var c in expectedPin)
                ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).ShouldBeTrue($"pin '{expectedPin}' must be lowercase hex");

            expectedPort.ShouldBeInRange(ProjectIdentity.MinPort, ProjectIdentity.MaxPort);
        }

        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void V2Pin_IsPrefixOfV2ProjectPathHash(string path, string expectedPin, int expectedPort)
        {
            _ = expectedPort;
            var hash = ProjectIdentity.DeriveProjectPathHashV2(path);
            hash.Length.ShouldBe(64);
            hash.ShouldStartWith(expectedPin);
        }

        /// <summary>
        /// The defining property of v2 (the B5 fix): a backslash project root and its forward-slash
        /// equivalent hash IDENTICALLY. Read straight from the committed <c>separatorEquivalence</c>
        /// block so the shared artifact and the C# reference can never silently drift.
        /// </summary>
        [Fact]
        public void Backslash_And_ForwardSlash_HashIdentically_UnderV2()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFilePath));
            foreach (var p in doc.RootElement.GetProperty("separatorEquivalence").GetProperty("pairs").EnumerateArray())
            {
                var backslash = p.GetProperty("backslash").GetString()!;
                var forward = p.GetProperty("forwardSlash").GetString()!;
                var pin = p.GetProperty("pin").GetString()!;
                var port = p.GetProperty("port").GetInt32();

                ProjectIdentity.DerivePinV2(backslash).ShouldBe(pin);
                ProjectIdentity.DerivePinV2(forward).ShouldBe(pin);
                ProjectIdentity.DerivePinV2(backslash).ShouldBe(ProjectIdentity.DerivePinV2(forward));

                ProjectIdentity.DerivePortV2(backslash).ShouldBe(port);
                ProjectIdentity.DerivePortV2(forward).ShouldBe(port);

                ProjectIdentity.DeriveProjectPathHashV2(backslash).ShouldBe(ProjectIdentity.DeriveProjectPathHashV2(forward));
            }
        }

        /// <summary>
        /// v1 and v2 MUST AGREE for a forward-slash-only path (no backslash to convert) — the seamless
        /// transition depends on POSIX/forward-slash configs being unchanged by v2.
        /// </summary>
        [Fact]
        public void V1_And_V2_Agree_ForForwardSlashOnlyPaths()
        {
            const string path = "/home/user/my-game";
            ProjectIdentity.DerivePinV2(path).ShouldBe(ProjectIdentity.DerivePin(path));
            ProjectIdentity.DerivePortV2(path).ShouldBe(ProjectIdentity.DerivePort(path));
            ProjectIdentity.DeriveProjectPathHashV2(path).ShouldBe(ProjectIdentity.DeriveProjectPathHash(path));
        }

        /// <summary>
        /// v1 and v2 MUST DIFFER for a backslash path — proving v2 actually changed the normalization
        /// (the whole point of B5). This is the legacy-vs-new hash the dual-hash transition relies on.
        /// </summary>
        [Fact]
        public void V1_And_V2_Differ_ForBackslashPaths()
        {
            var backslash = "C:" + Bs + "Users" + Bs + "user" + Bs + "my-game";
            ProjectIdentity.DerivePinV2(backslash).ShouldNotBe(ProjectIdentity.DerivePin(backslash));
            ProjectIdentity.DeriveProjectPathHashV2(backslash).ShouldNotBe(ProjectIdentity.DeriveProjectPathHash(backslash));
        }

        [Fact]
        public void NormalizeV2_TrimsTrailingSeparators_ConvertsBackslash_AndLowercases()
        {
            ProjectIdentity.NormalizeV2("C:" + Bs + "Users" + Bs + "User" + Bs).ShouldBe("c:/users/user");
            ProjectIdentity.NormalizeV2("/Home/User/My-Game/").ShouldBe("/home/user/my-game");
        }

        [Fact]
        public void UnicodeDivergenceV2_CanonicalIsCSharp_AndDiffersFromNaiveJs()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GoldenFilePath));
            foreach (var c in doc.RootElement.GetProperty("unicodeDivergence").GetProperty("cases").EnumerateArray())
            {
                var path = c.GetProperty("path").GetString()!;
                var canonical = c.GetProperty("canonical");
                var jsNaive = c.GetProperty("jsNaiveToLowerCase");

                ProjectIdentity.DerivePinV2(path).ShouldBe(canonical.GetProperty("pin").GetString());
                ProjectIdentity.DerivePortV2(path).ShouldBe(canonical.GetProperty("port").GetInt32());

                ProjectIdentity.DerivePortV2(path).ShouldNotBe(jsNaive.GetProperty("port").GetInt32());
                ProjectIdentity.DerivePinV2(path).ShouldNotBe(jsNaive.GetProperty("pin").GetString());
            }
        }

        [Fact]
        public void DeriveV2_NullPath_Throws()
        {
            Should.Throw<System.ArgumentNullException>(() => ProjectIdentity.DerivePinV2(null!));
            Should.Throw<System.ArgumentNullException>(() => ProjectIdentity.DerivePortV2(null!));
            Should.Throw<System.ArgumentNullException>(() => ProjectIdentity.DeriveProjectPathHashV2(null!));
            Should.Throw<System.ArgumentNullException>(() => ProjectIdentity.NormalizeV2(null!));
            Should.Throw<System.ArgumentNullException>(() => ProjectIdentity.DeriveV2(null!));
        }

        // ---- auth-fixes T1 / defect B: the whole-identity v2 factory. ----

        /// <summary>
        /// Every golden vector, resolved through the whole-identity <see cref="ProjectIdentity.DeriveV2"/>
        /// factory: pin AND port must match the standalone v2 primitives, so an identity object can
        /// never carry a pin and a port derived from different normalizations.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenVectors))]
        public void DeriveV2_ProducesV2PinAndV2Port_ForEveryGoldenVector(string path, string expectedPin, int expectedPort)
        {
            var identity = ProjectIdentity.DeriveV2(path);

            identity.Pin.ShouldBe(expectedPin);
            identity.Port.ShouldBe(expectedPort);
            identity.PortIsOverridden.ShouldBeFalse();
        }

        /// <summary>
        /// The defect-B invariant at the identity level: a backslash root and its forward-slash form
        /// yield the same pin AND the same port, and that port is NOT the v1 derivation.
        /// </summary>
        [Fact]
        public void DeriveV2_BackslashAndForwardSlash_AgreeOnBothPinAndPort()
        {
            var backslash = "C:" + Bs + "tmp" + Bs + "mcpauth-test" + Bs + "test-project";
            var forward = backslash.Replace(Bs, "/");

            var fromBackslash = ProjectIdentity.DeriveV2(backslash);
            var fromForward = ProjectIdentity.DeriveV2(forward);

            fromBackslash.Pin.ShouldBe(fromForward.Pin);
            fromBackslash.Port.ShouldBe(fromForward.Port);

            // v1 would have split them: same object, two different pre-hash strings.
            fromBackslash.Port.ShouldNotBe(ProjectIdentity.Derive(backslash).Port);
            fromBackslash.Pin.ShouldNotBe(ProjectIdentity.Derive(backslash).Pin);
        }

        /// <summary>
        /// The marker's <c>portOverride</c> keeps precedence under v2 (unchanged behaviour); the pin
        /// stays hash-derived from the v2 normalization.
        /// </summary>
        [Fact]
        public void DeriveV2_PortOverride_Wins_AndPinStaysHashDerived()
        {
            var backslash = "C:" + Bs + "tmp" + Bs + "mcpauth-test" + Bs + "test-project";

            var overridden = ProjectIdentity.DeriveV2(backslash, portOverride: 27618);
            overridden.Port.ShouldBe(27618);
            overridden.PortIsOverridden.ShouldBeTrue();
            overridden.Pin.ShouldBe(ProjectIdentity.DerivePinV2(backslash));

            var viaMarker = ProjectIdentity.DeriveV2(backslash, new ProjectMarker { PortOverride = 27618 });
            viaMarker.Port.ShouldBe(27618);
            viaMarker.PortIsOverridden.ShouldBeTrue();

            var noOverride = ProjectIdentity.DeriveV2(backslash, (ProjectMarker?)null);
            noOverride.Port.ShouldBe(ProjectIdentity.DerivePortV2(backslash));
            noOverride.PortIsOverridden.ShouldBeFalse();
        }

        private static string Bs => ((char)92).ToString();
    }
}
