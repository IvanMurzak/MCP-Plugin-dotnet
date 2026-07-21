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
using System.Linq;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig.Impl;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// zero-config-engine-connect b3 (design 02 § D21 / OQ3) — the shared config writer pins its
    /// deterministic-port emission to the <b>v1</b> derivation for the Unreal local plane (so the written
    /// port matches the v1 port the Unreal .NET sidecar binds via <c>ProjectConnectionResolver</c>) while
    /// keeping the routing <b>pin</b> (registration hash) on v2 for EVERY engine. Non-Unreal engines'
    /// emission is byte-identical to before (Unity / Godot stay on v2).
    ///
    /// <para>The interesting case is a Windows <b>backslash</b> project root, where v1 and v2 diverge.
    /// Every backslash root here is a hardcoded literal built from <c>(char)92</c> — never
    /// <c>Path.GetTempPath()</c> — so the divergence is real on Linux CI too (a temp path is forward-slash,
    /// where v1 == v2 and the whole point of this task would hide). The roots are HASH INPUTS only; they
    /// are never created on disk, so identity resolution reads no marker and stays on the derived-port
    /// branch.</para>
    /// </summary>
    public class AgentConfiguratorSettingsPortDerivationTests
    {
        // A literal backslash, built from its char code so no escaping subtlety can alter it.
        private static string Bs => ((char)92).ToString();

        // C:\Users\user\my-game — the committed golden-vector backslash root (both golden files carry it):
        //   v1 (ProjectIdentity.GoldenVectors.json)     -> pin 8ef72cf7, port 29310  (the Unreal binder)
        //   v2 (ProjectIdentity.GoldenVectors.v2.json)  -> pin 5a87324e, port 24298  (Unity / Godot + the pin)
        private static string BackslashRoot => "C:" + Bs + "Users" + Bs + "user" + Bs + "my-game";

        private static AgentConfiguratorSettings Settings(
            string root,
            LocalPortDerivation localPortDerivation,
            string host = "http://localhost/mcp",
            ConnectionMode connectionMode = ConnectionMode.Local) => new(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: root,
                executableFullPath: "C:/Tools/ai-game-developer-mcp-server.exe",
                port: 50000,
                timeoutMs: 30000,
                host: host,
                connectionMode: connectionMode,
                localPortDerivation: localPortDerivation);

        private static string NewTempDir()
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            return root;
        }

        // ---- DoD 1: the writer emits v1 ports for Unreal; writer == the v1 binder for a backslash root. ----

        /// <summary>
        /// The headline D21 assertion: on <see cref="LocalPortDerivation.V1"/> (Unreal) the writer emits the
        /// v1-derived port — byte-equal to what the Unreal sidecar binder (<c>ProjectConnectionResolver</c>,
        /// v1 <see cref="ProjectIdentity.Derive"/>) resolves — and NOT the v2 port; the routing pin stays v2.
        /// The v1/v2 port divergence is only visible on a backslash root.
        /// </summary>
        [Fact]
        public void Unreal_WriterEmitsV1DerivedPort_MatchingTheV1Binder_AndKeepsV2Pin_ForBackslashRoot()
        {
            var settings = Settings(BackslashRoot, LocalPortDerivation.V1);

            settings.LocalPortDerivation.ShouldBe(LocalPortDerivation.V1);
            settings.Identity.PortIsOverridden.ShouldBeFalse(); // no marker on disk => the derived branch

            // Port == the v1 binder derivation == the committed v1 golden vector value.
            settings.ResolvedPort.ShouldBe(ProjectIdentity.Derive(BackslashRoot).Port);
            settings.ResolvedPort.ShouldBe(ProjectIdentity.DerivePort(BackslashRoot));
            settings.ResolvedPort.ShouldBe(29310); // ProjectIdentity.GoldenVectors.json, C:\Users\user\my-game

            // ...and NOT the v2 port the default (Unity/Godot) writer would emit.
            settings.ResolvedPort.ShouldNotBe(ProjectIdentity.DerivePortV2(BackslashRoot));

            // The routing pin (registration hash) stays v2 for Unreal too (DoD 3, previewed here).
            settings.ProjectPin.ShouldBe(ProjectIdentity.DerivePinV2(BackslashRoot));
            settings.ProjectPin.ShouldBe("5a87324e"); // ProjectIdentity.GoldenVectors.v2.json
            settings.ProjectPin.ShouldNotBe(ProjectIdentity.DerivePin(BackslashRoot)); // != the v1 pin

            // Portless host => PinnedPort falls through to the (v1) derived port, and it reaches the file.
            settings.PinnedPort.ShouldBe(ProjectIdentity.Derive(BackslashRoot).Port);

            var c = new ClaudeCodeConfigurator();
            var http = c.GetHttpConfig(settings).ExpectedFileContent;
            http.ShouldContain($"\"url\": \"http://localhost:{ProjectIdentity.DerivePort(BackslashRoot)}/mcp/p/{ProjectIdentity.DerivePinV2(BackslashRoot)}\"");
            http.ShouldNotContain($"localhost:{ProjectIdentity.DerivePortV2(BackslashRoot)}"); // never the v2 port

            var stdio = c.GetStdioConfig(settings).ExpectedFileContent;
            stdio.ShouldContain($"{Consts.MCP.Server.Args.Port}={ProjectIdentity.DerivePort(BackslashRoot)}");
            stdio.ShouldContain($"{Consts.MCP.Server.Args.Project}={ProjectIdentity.DerivePinV2(BackslashRoot)}");
        }

        /// <summary>
        /// The same writer==binder property, but anchored to the committed cross-language golden-vector
        /// files rather than hardcoded numbers: for EVERY backslash vector in the golden files, the Unreal
        /// (v1) writer reproduces the v1 golden port (== the v1 binder), the default (v2) writer reproduces
        /// the v2 golden port, and both reproduce the v2 golden pin. This is the "golden-vector test covers
        /// backslash roots writer==binder" DoD item.
        /// </summary>
        [Fact]
        public void WriterVsBinder_ForEveryBackslashGoldenVector_UnrealIsV1_OthersAreV2_PinStaysV2()
        {
            var v1 = LoadGoldenVectors("ProjectIdentity.GoldenVectors.json");
            var v2 = LoadGoldenVectors("ProjectIdentity.GoldenVectors.v2.json");

            var backslashPaths = v1.Keys.Where(p => p.IndexOf(Bs, StringComparison.Ordinal) >= 0).ToList();
            backslashPaths.ShouldNotBeEmpty("the committed golden files must carry at least one backslash vector");

            foreach (var path in backslashPaths)
            {
                var (_, v1Port) = v1[path];
                var (v2Pin, v2Port) = v2[path];

                // The golden files themselves must disagree on the port for a backslash root — that
                // divergence (v1 binder vs v2 writer) is the entire reason this task exists.
                v1Port.ShouldNotBe(v2Port);

                var unreal = Settings(path, LocalPortDerivation.V1);
                var others = Settings(path, LocalPortDerivation.V2);

                // Unreal writer == v1 golden port == the v1 binder derivation.
                unreal.ResolvedPort.ShouldBe(v1Port, customMessage: $"Unreal writer must emit the v1 port for '{path}'");
                unreal.ResolvedPort.ShouldBe(ProjectIdentity.Derive(path).Port);

                // Non-Unreal writer == the v2 golden port (unchanged emission).
                others.ResolvedPort.ShouldBe(v2Port, customMessage: $"non-Unreal writer must emit the v2 port for '{path}'");

                // Both keep the v2 registration pin.
                unreal.ProjectPin.ShouldBe(v2Pin);
                others.ProjectPin.ShouldBe(v2Pin);
            }
        }

        // ---- DoD 2: non-Unreal engines' emission is byte-identical (Unity / Godot regression). ----

        /// <summary>
        /// The default (no <c>localPortDerivation</c> argument) is <see cref="LocalPortDerivation.V2"/> and
        /// emits exactly what an explicit V2 emits — the same v2 port and the same written config bytes —
        /// so existing Unity / Godot callers are entirely unaffected by the new knob.
        /// </summary>
        [Fact]
        public void NonUnreal_DefaultEqualsExplicitV2_AndEmitsUnchangedV2Port_ForBackslashRoot()
        {
            var byDefault = new AgentConfiguratorSettings(
                operatingSystem: OperatingSystemKind.Windows,
                projectRootPath: BackslashRoot,
                executableFullPath: "C:/Tools/ai-game-developer-mcp-server.exe",
                port: 50000,
                timeoutMs: 30000,
                host: "http://localhost/mcp"); // NO localPortDerivation arg => defaults to V2

            var explicitV2 = Settings(BackslashRoot, LocalPortDerivation.V2);

            byDefault.LocalPortDerivation.ShouldBe(LocalPortDerivation.V2);

            // Byte-identical resolution between the default and an explicit V2.
            byDefault.ResolvedPort.ShouldBe(explicitV2.ResolvedPort);
            byDefault.PinnedPort.ShouldBe(explicitV2.PinnedPort);
            byDefault.ProjectPin.ShouldBe(explicitV2.ProjectPin);

            // ...and it is the v2 derivation, unchanged from before this task.
            byDefault.ResolvedPort.ShouldBe(ProjectIdentity.DerivePortV2(BackslashRoot));
            byDefault.ResolvedPort.ShouldBe(24298); // ProjectIdentity.GoldenVectors.v2.json
            byDefault.ProjectPin.ShouldBe(ProjectIdentity.DerivePinV2(BackslashRoot));

            // The written config bytes are identical for the default and the explicit-V2 settings.
            var c = new ClaudeCodeConfigurator();
            c.GetHttpConfig(byDefault).ExpectedFileContent.ShouldBe(c.GetHttpConfig(explicitV2).ExpectedFileContent);
            c.GetStdioConfig(byDefault).ExpectedFileContent.ShouldBe(c.GetStdioConfig(explicitV2).ExpectedFileContent);
        }

        // ---- DoD 3: the registration hash / pin path is untouched (v2) regardless of port derivation. ----

        /// <summary>
        /// For one and the same backslash root the v1 (Unreal) and v2 (default) writers emit DIFFERENT
        /// ports but the SAME v2 routing pin — the registration hash / pin path stays v2 for every engine.
        /// </summary>
        [Fact]
        public void RegistrationPin_StaysV2_WhilePortDiverges_BetweenV1AndV2_ForBackslashRoot()
        {
            var v1 = Settings(BackslashRoot, LocalPortDerivation.V1);
            var v2 = Settings(BackslashRoot, LocalPortDerivation.V2);

            // The routing pin (registration hash) is the SAME v2 value for both engines.
            v1.ProjectPin.ShouldBe(v2.ProjectPin);
            v1.ProjectPin.ShouldBe(ProjectIdentity.DerivePinV2(BackslashRoot));

            // ...but the emitted PORT diverges: v1 for Unreal, v2 for the rest.
            v1.ResolvedPort.ShouldNotBe(v2.ResolvedPort);
            v1.ResolvedPort.ShouldBe(ProjectIdentity.DerivePort(BackslashRoot));   // v1
            v2.ResolvedPort.ShouldBe(ProjectIdentity.DerivePortV2(BackslashRoot)); // v2

            // The /p/<pin> routing segment in BOTH written configs is the v2 pin (registration path untouched).
            var c = new ClaudeCodeConfigurator();
            c.GetHttpConfig(v1).ExpectedFileContent.ShouldContain($"/p/{ProjectIdentity.DerivePinV2(BackslashRoot)}");
            c.GetHttpConfig(v2).ExpectedFileContent.ShouldContain($"/p/{ProjectIdentity.DerivePinV2(BackslashRoot)}");
            c.GetStdioConfig(v1).ExpectedFileContent.ShouldContain($"{Consts.MCP.Server.Args.Project}={ProjectIdentity.DerivePinV2(BackslashRoot)}");
        }

        // ---- The v1 (Unreal) path preserves the 3-level precedence — only level 3's derivation changed. ----

        /// <summary>
        /// Level 1 (marker <c>portOverride</c>) still wins outright on the v1 path — over both the typed
        /// host port (level 2) and the v1 derivation (level 3). The override is not hash-dependent, so this
        /// is unchanged by the engine-aware derivation. Uses a real on-disk marker (temp dir).
        /// </summary>
        [Fact]
        public void Unreal_MarkerPortOverride_StillWins_OverV1Derivation_AndTypedHostPort()
        {
            var root = NewTempDir();
            try
            {
                const int overridePort = 27777;
                new ProjectMarker { PortOverride = overridePort }.Write(root);

                var settings = Settings(root, LocalPortDerivation.V1, host: "http://localhost:50000/mcp");

                settings.ExplicitHostPort.ShouldBe(50000);
                settings.Identity.PortIsOverridden.ShouldBeTrue();
                settings.ResolvedPort.ShouldBe(overridePort); // level 1 beats the v1 derivation
                settings.PinnedPort.ShouldBe(overridePort);   // level 1 beats the typed host port too

                var stdio = new ClaudeCodeConfigurator().GetStdioConfig(settings).ExpectedFileContent;
                stdio.ShouldContain($"{Consts.MCP.Server.Args.Port}={overridePort}");
                stdio.ShouldNotContain($"{Consts.MCP.Server.Args.Port}=50000");
            }
            finally { Directory.Delete(root, recursive: true); }
        }

        /// <summary>
        /// Level 2 (a port typed into a loopback <see cref="AgentConfiguratorSettings.Host"/>) is honoured
        /// on the v1 path exactly as on the v2 path — only the level-3 derived FALLBACK switched version.
        /// So an explicit host port beats the v1 derivation just as it beats the v2 one.
        /// </summary>
        [Fact]
        public void Unreal_TypedHostPort_IsHonoured_OverTheV1Derivation()
        {
            var settings = Settings(BackslashRoot, LocalPortDerivation.V1, host: "http://localhost:50000/mcp");

            settings.Identity.PortIsOverridden.ShouldBeFalse();
            settings.ExplicitHostPort.ShouldBe(50000);
            settings.PinnedPort.ShouldBe(50000); // the typed port, not the v1 derived fallback
            settings.PinnedPort.ShouldNotBe(ProjectIdentity.DerivePort(BackslashRoot));
            settings.PinnedHttpUrl.ShouldBe($"http://localhost:50000/mcp/p/{ProjectIdentity.DerivePinV2(BackslashRoot)}");
        }

        // --- helpers ---

        private static Dictionary<string, (string Pin, int Port)> LoadGoldenVectors(string fileName)
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, fileName);
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var map = new Dictionary<string, (string, int)>();
            foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                map[v.GetProperty("path").GetString()!] =
                    (v.GetProperty("pin").GetString()!, v.GetProperty("port").GetInt32());
            }
            return map;
        }
    }
}
