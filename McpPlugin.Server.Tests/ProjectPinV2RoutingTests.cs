/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak)                    │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// End-to-end routing coverage for the pin v2 / dual-hash transition (auth-fixes T3 / defect B5),
    /// exercising <see cref="AccountInstances.Resolve"/> with REAL path-derived hashes rather than
    /// synthetic strings. A "new plugin" registers with BOTH the v2 (separator-normalized) hash and the
    /// v1 legacy hash of its reported project root (as <see cref="ConnectionInstanceMetadata.Create"/>
    /// builds it); config pins are derived from the config's project root the same way Editor Configure
    /// / cli-core setup-mcp (v2) and an OLD <c>.mcp.json</c> (v1) do. Covers:
    /// <list type="bullet">
    ///   <item>the B5 fix — a Windows backslash-root plugin and a forward-slash-root config now route
    ///   together under v2 (they diverged under v1);</item>
    ///   <item>the compat pairs — old-config(v1-pin)+new-plugin and new-config(v2-pin)+new-plugin;</item>
    ///   <item>the precedence chain pin(strict) → sticky → single → MRU with path-derived pins;</item>
    ///   <item>the cross-account isolation regression — a pin never crosses the <c>sub</c> bucket.</item>
    /// </list>
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ProjectPinV2RoutingTests
    {
        const string PosixA = "/home/dev/GameA";
        const string PosixB = "/home/dev/GameB";
        static string WinBackslash => "C:" + Bs + "Users" + Bs + "dev" + Bs + "MyGame";
        const string WinForward = "C:/Users/dev/MyGame";

        static string Bs => ((char)92).ToString();

        sealed class Clock
        {
            public DateTimeOffset Now;
            public Clock(DateTimeOffset start) => Now = start;
            public void Advance(TimeSpan by) => Now += by;
        }

        static (AccountInstances reg, Clock clock) NewRegistry()
        {
            var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            return (new AccountInstances(() => clock.Now), clock);
        }

        /// <summary>
        /// A "new plugin" handshake: carries the v2 hash as the primary <c>ProjectPathHash</c> and the
        /// v1 hash as <c>ProjectPathHashLegacy</c> — exactly what <see cref="ConnectionInstanceMetadata.Create"/>
        /// sends for <paramref name="pluginReportedRoot"/>.
        /// </summary>
        static PluginInstanceMetadata NewPlugin(
            string pluginReportedRoot,
            string instanceId = "i1",
            string engine = "unity",
            string project = "MyGame",
            string machine = "PC-1")
            => new PluginInstanceMetadata(
                instanceId,
                engine,
                project,
                ProjectIdentity.DeriveProjectPathHashV2(pluginReportedRoot),
                machine,
                ProjectIdentity.DeriveProjectPathHash(pluginReportedRoot));

        /// <summary>
        /// An OLD (pre-dual-hash) plugin: it sent only the v1 hash of its reported root as
        /// <c>ProjectPathHash</c>, with no legacy field. Models the world before this change.
        /// </summary>
        static PluginInstanceMetadata OldPlugin(
            string pluginReportedRoot,
            string instanceId = "i1",
            string engine = "unity",
            string project = "MyGame",
            string machine = "PC-1")
            => new PluginInstanceMetadata(
                instanceId,
                engine,
                project,
                ProjectIdentity.DeriveProjectPathHash(pluginReportedRoot),
                machine);

        // The pin a NEW config (Editor Configure / cli-core setup-mcp) writes for a project root.
        static string V2Pin(string configRoot) => ProjectIdentity.DerivePinV2(configRoot);

        // The pin an OLD (v1) config already on disk carries for a project root.
        static string V1Pin(string configRoot) => ProjectIdentity.DerivePin(configRoot);

        // ─────────────────────── Compat pair: new-config (v2 pin) + new-plugin ───────────────────────

        [Theory]
        [InlineData(PosixA)]
        [InlineData(WinForward)]
        public void NewConfig_V2Pin_NewPlugin_SamePath_Resolves(string path)
        {
            var (reg, _) = NewRegistry();
            var instance = reg.Register("acc-1", NewPlugin(path), "conn-1");

            var res = reg.Resolve("acc-1", V2Pin(path), selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(instance.InstanceId);
        }

        // ─────────────────── Compat pair: old-config (v1 pin) + new-plugin (legacy) ───────────────────

        [Theory]
        [InlineData(PosixA)]
        [InlineData(WinForward)]
        public void OldConfig_V1Pin_NewPlugin_Resolves_ViaLegacyHash(string path)
        {
            // A user with an OLD .mcp.json still on disk (v1 pin) connecting a NEW plugin must keep
            // routing — the plugin's v1 legacy hash is what the old pin prefix-matches.
            var (reg, _) = NewRegistry();
            var instance = reg.Register("acc-1", NewPlugin(path), "conn-1");

            var res = reg.Resolve("acc-1", V1Pin(path), selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(instance.InstanceId);
        }

        // ─────────────────────────── The B5 fix: separators no longer divide ──────────────────────────

        [Fact]
        public void B5Fix_WindowsBackslashPlugin_ForwardSlashConfig_Resolves_UnderV2()
        {
            // Plugin reports a Windows backslash root; the config was generated from the forward-slash
            // form of the SAME project. Under v2 both normalize identically, so the v2 pin matches.
            var (reg, _) = NewRegistry();
            var instance = reg.Register("acc-1", NewPlugin(WinBackslash), "conn-1");

            var res = reg.Resolve("acc-1", V2Pin(WinForward), selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(instance.InstanceId);
        }

        [Fact]
        public void B5_PreV2_ForwardSlashConfig_DidNotMatch_OldBackslashPlugin()
        {
            // Regression witness of the pre-fix world: an OLD plugin (single v1 hash of its backslash
            // root) + a forward-slash config could NOT route on Windows — the forward-slash pin is not
            // a prefix of the backslash v1 hash. That WAS B5; the v2 path above is what recovers it.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", OldPlugin(WinBackslash), "conn-1");

            reg.Resolve("acc-1", V1Pin(WinForward), selectedInstanceId: null)
                .Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
            reg.Resolve("acc-1", V2Pin(WinForward), selectedInstanceId: null)
                .Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
        }

        // ───────────────────────────────── Precedence: pin is strict ─────────────────────────────────

        [Fact]
        public void Pin_IsStrict_NeverFallsThroughToAnotherProject()
        {
            var (reg, _) = NewRegistry();
            var a = reg.Register("acc-1", NewPlugin(PosixA, instanceId: "a", project: "GameA"), "conn-a");
            reg.Register("acc-1", NewPlugin(PosixB, instanceId: "b", project: "GameB"), "conn-b");

            // Pinned to A resolves ONLY to A even though B is live.
            var res = reg.Resolve("acc-1", V2Pin(PosixA), selectedInstanceId: null);
            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(a.InstanceId);
        }

        [Fact]
        public void Pin_MatchingNoInstance_ButAccountNonEmpty_IsNoMatchPinned()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", NewPlugin(PosixA), "conn-a");

            var res = reg.Resolve("acc-1", V2Pin(PosixB), selectedInstanceId: null); // B's editor is closed
            res.Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
        }

        // ─────────────────────────────── Precedence: sticky within pin ───────────────────────────────

        [Fact]
        public void Sticky_NarrowsWithinPinnedCandidateSet()
        {
            // Two live instances of the SAME project (different machines → distinct dedup keys), so a
            // pin matches BOTH; the sticky selectedInstanceId picks one.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", NewPlugin(WinForward, instanceId: "m1", machine: "PC-1"), "conn-1");
            var m2 = reg.Register("acc-1", NewPlugin(WinForward, instanceId: "m2", machine: "PC-2"), "conn-2");

            var res = reg.Resolve("acc-1", V2Pin(WinForward), selectedInstanceId: "m2");

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(m2.InstanceId);
        }

        // ─────────────────────────────── Precedence: single auto-pair ────────────────────────────────

        [Fact]
        public void Single_PinnedCandidate_AutoPairs()
        {
            var (reg, _) = NewRegistry();
            var only = reg.Register("acc-1", NewPlugin(PosixA), "conn-a");

            var res = reg.Resolve("acc-1", V2Pin(PosixA), selectedInstanceId: null);
            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(only.InstanceId);
            res.AdvisoryNote.ShouldBeNull(); // single candidate → no MRU advisory
        }

        // ──────────────────────────────── Precedence: MRU when many ──────────────────────────────────

        [Fact]
        public void MultiplePinnedCandidates_Unresolved_PickMostRecentlyActive_WithAdvisory()
        {
            var (reg, clock) = NewRegistry();
            var m1 = reg.Register("acc-1", NewPlugin(WinForward, instanceId: "m1", machine: "PC-1"), "conn-1");
            reg.Register("acc-1", NewPlugin(WinForward, instanceId: "m2", machine: "PC-2"), "conn-2");

            // Make m1 the most-recently-active.
            clock.Advance(TimeSpan.FromMinutes(5));
            reg.TouchByConnection("conn-1");

            var res = reg.Resolve("acc-1", V2Pin(WinForward), selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe(m1.InstanceId);
            res.AdvisoryNote.ShouldNotBeNull(); // one-time "routed to … switch with select_engine_instance" note
        }

        // ──────────────────── Cross-account isolation regression (bucket-by-sub) ──────────────────────

        [Fact]
        public void Pin_NeverCrossesAccounts_EmptyOtherAccount_IsAccountEmpty()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", NewPlugin(PosixA), "conn-a");

            // Account 2 has no live instances; account-1's pin must not reach into account-1's bucket.
            var res = reg.Resolve("acc-2", V2Pin(PosixA), selectedInstanceId: null);
            res.Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);
        }

        [Fact]
        public void Pin_NeverCrossesAccounts_OtherAccountHasOwnInstance_IsNoMatchPinned()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", NewPlugin(PosixA, project: "GameA"), "conn-a"); // account 1 owns GameA

            // Account 2 owns a DIFFERENT project and pins account-1's project — it must never resolve
            // account-1's instance; it fails closed with NoMatchPinned (its own bucket has no match).
            reg.Register("acc-2", NewPlugin(PosixB, project: "GameB"), "conn-b");
            var res = reg.Resolve("acc-2", V2Pin(PosixA), selectedInstanceId: null);
            res.Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
            res.Instance.ShouldBeNull();
        }
    }
}
