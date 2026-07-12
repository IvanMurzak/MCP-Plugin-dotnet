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
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Registry-lifecycle, resolution-precedence, strict-pin-matrix and multi-account isolation tests
    /// for the account+instance pairing plane (mcp-authorize b3, design doc 04). Exercises
    /// <see cref="AccountInstances"/> directly with a deterministic clock.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class AccountInstancesTests
    {
        // Distinct project path hashes; the pin is the first 8 hex chars of each (prefix match).
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "99887766554433221100ffeeddccbbaa99887766554433221100ffeeddccbbaa";
        const string PinB = "99887766";

        sealed class Clock
        {
            public DateTimeOffset Now;
            public Clock(DateTimeOffset start) => Now = start;
            public void Advance(TimeSpan by) => Now += by;
        }

        static PluginInstanceMetadata Meta(
            string instanceId,
            string engine = "unity",
            string project = "MyGame",
            string pathHash = HashA,
            string machine = "PC-1")
            => new PluginInstanceMetadata(instanceId, engine, project, pathHash, machine);

        static (AccountInstances reg, Clock clock) NewRegistry()
        {
            var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            return (new AccountInstances(() => clock.Now), clock);
        }

        // ─────────────────────────── Registry lifecycle ───────────────────────────

        [Fact]
        public void Register_AddsInstance_ScopedToAccount()
        {
            var (reg, _) = NewRegistry();
            var inst = reg.Register("acc-1", Meta("i1"), "conn-1");

            inst.InstanceId.ShouldBe("i1");
            inst.ConnectionId.ShouldBe("conn-1");
            reg.InstanceCount("acc-1").ShouldBe(1);
            reg.AccountCount.ShouldBe(1);
            reg.GetAccountByConnection("conn-1").ShouldBe("acc-1");
        }

        [Fact]
        public void Register_SameInstanceId_ReconnectReplaces_KeepsSlot_UpdatesConnection()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-old");
            var replaced = reg.Register("acc-1", Meta("i1"), "conn-new");

            reg.InstanceCount("acc-1").ShouldBe(1); // same slot, not a second entry
            replaced.ConnectionId.ShouldBe("conn-new");
            reg.GetAccountByConnection("conn-new").ShouldBe("acc-1");
            // The prior connection's reverse index is cleared so a late disconnect can't evict it.
            reg.GetAccountByConnection("conn-old").ShouldBeNull();
        }

        [Fact]
        public void Register_NewInstanceId_SameDedupKey_EvictsStale()
        {
            // Editor restart: same (path, engine, machine), new InstanceId → the stale entry is evicted.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-1");
            reg.Register("acc-1", Meta("i2"), "conn-2"); // same dedup key (default Meta args), new id

            reg.InstanceCount("acc-1").ShouldBe(1);
            reg.GetInstanceByConnection("conn-2").ShouldNotBeNull();
            reg.GetAccountByConnection("conn-1").ShouldBeNull(); // stale connection dropped
        }

        [Fact]
        public void Register_NewInstanceId_DifferentMachine_Coexist()
        {
            // Two editors on the SAME project on DIFFERENT machines have distinct dedup keys → coexist.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", machine: "PC-1"), "conn-1");
            reg.Register("acc-1", Meta("i2", machine: "PC-2"), "conn-2");

            reg.InstanceCount("acc-1").ShouldBe(2);
        }

        [Fact]
        public void RemoveByConnection_RemovesInstance_AndDropsEmptyBucket()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-1");

            var removed = reg.RemoveByConnection("conn-1");

            removed.ShouldNotBeNull();
            removed!.Value.AccountId.ShouldBe("acc-1");
            removed.Value.InstanceId.ShouldBe("i1");
            reg.InstanceCount("acc-1").ShouldBe(0);
            reg.AccountCount.ShouldBe(0); // empty account bucket dropped (no unbounded growth)
        }

        [Fact]
        public void RemoveByConnection_StaleConnectionAfterReconnect_IsNoOp()
        {
            // No-orphaning: after a reconnect-replace, a late disconnect for the OLD connection must
            // NOT evict the (now reconnected) instance.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-old");
            reg.Register("acc-1", Meta("i1"), "conn-new");

            var removed = reg.RemoveByConnection("conn-old");

            removed.ShouldBeNull();
            reg.InstanceCount("acc-1").ShouldBe(1);
            reg.GetInstanceByConnection("conn-new").ShouldNotBeNull();
        }

        [Fact]
        public void RemoveByConnection_Unknown_ReturnsNull()
        {
            var (reg, _) = NewRegistry();
            reg.RemoveByConnection("nope").ShouldBeNull();
        }

        // ─────────────────────────── Resolution: single / MRU / sticky ───────────────────────────

        [Fact]
        public void Resolve_AccountEmpty_WhenAccountHasNoInstances()
        {
            var (reg, _) = NewRegistry();
            var res = reg.Resolve("acc-unknown", projectPin: null, selectedInstanceId: null);
            res.Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);
            res.Instance.ShouldBeNull();
        }

        [Fact]
        public void Resolve_SingleInstance_AutoPairs_NoAdvisoryNote()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-1");

            var res = reg.Resolve("acc-1", null, null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1");
            res.AdvisoryNote.ShouldBeNull();
        }

        [Fact]
        public void Resolve_MultipleUnpinned_PicksMostRecentlyActive_WithAdvisoryNote()
        {
            var (reg, clock) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "GameA", pathHash: HashA), "conn-1");
            clock.Advance(TimeSpan.FromSeconds(5));
            reg.Register("acc-1", Meta("i2", project: "GameB", pathHash: HashB), "conn-2"); // most recent

            var res = reg.Resolve("acc-1", null, null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i2");
            res.AdvisoryNote.ShouldNotBeNull();
            res.AdvisoryNote!.ShouldContain("GameB");
        }

        [Fact]
        public void Resolve_Sticky_SelectsThatInstance()
        {
            var (reg, clock) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "GameA", pathHash: HashA), "conn-1"); // older
            clock.Advance(TimeSpan.FromSeconds(5));
            reg.Register("acc-1", Meta("i2", project: "GameB", pathHash: HashB), "conn-2"); // MRU

            // Sticky selects the OLDER instance, overriding MRU.
            var res = reg.Resolve("acc-1", null, selectedInstanceId: "i1");

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1");
            res.AdvisoryNote.ShouldBeNull();
        }

        [Fact]
        public void Resolve_StickyToDeadInstance_FallsBackToPrecedence()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-1");

            // Selected instance no longer alive → ignored; single instance auto-pairs.
            var res = reg.Resolve("acc-1", null, selectedInstanceId: "dead");

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1");
        }

        // ─────────────────────────── Strict-pin matrix ───────────────────────────

        [Fact]
        public void Resolve_Pin_Match_Resolves()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", pathHash: HashA), "conn-1");

            var res = reg.Resolve("acc-1", projectPin: PinA, selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1");
        }

        [Fact]
        public void Resolve_Pin_NoMatch_WithOtherInstances_ReturnsNoMatchPinned()
        {
            // Account has live instances, but NONE match the pin — the pinned project's editor is closed.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "GameA", pathHash: HashA), "conn-1");
            reg.Register("acc-1", Meta("i2", project: "GameB", pathHash: HashB, machine: "PC-2"), "conn-2");

            const string UnmatchedPin = "deadbeef";
            var res = reg.Resolve("acc-1", projectPin: UnmatchedPin, selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
            res.Instance.ShouldBeNull();
        }

        [Fact]
        public void Resolve_Pin_NeverFallsThrough_EvenWhenSoleInstanceIsAnotherProject()
        {
            // The KEY strict-pin invariant: a pin NEVER routes to another project, even when that
            // other project's instance is the ONLY live instance.
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "OtherGame", pathHash: HashB), "conn-1");

            var res = reg.Resolve("acc-1", projectPin: PinA, selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
            res.Instance.ShouldBeNull();
        }

        [Fact]
        public void Resolve_Pin_Absent_ResolvesNormally()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", pathHash: HashA), "conn-1");

            var res = reg.Resolve("acc-1", projectPin: null, selectedInstanceId: null);

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1");
        }

        [Fact]
        public void Resolve_Pin_NarrowsCandidates_ThenStickyWithin()
        {
            // Two editors on the same project (same pin, different machines) → pin narrows to both,
            // sticky narrows further to the selected one.
            var (reg, clock) = NewRegistry();
            reg.Register("acc-1", Meta("i1", pathHash: HashA, machine: "PC-1"), "conn-1");
            clock.Advance(TimeSpan.FromSeconds(5));
            reg.Register("acc-1", Meta("i2", pathHash: HashA, machine: "PC-2"), "conn-2"); // MRU within pin

            var res = reg.Resolve("acc-1", projectPin: PinA, selectedInstanceId: "i1");

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1"); // sticky wins over MRU, both within the pin
        }

        [Fact]
        public void Resolve_Pin_StickyToDifferentProject_IsIgnored_PinWins()
        {
            // Sticky points at an instance OUTSIDE the pinned project → ignored (pin never overridden).
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "Pinned", pathHash: HashA, machine: "PC-1"), "conn-1");
            reg.Register("acc-1", Meta("i2", project: "Other", pathHash: HashB, machine: "PC-2"), "conn-2");

            var res = reg.Resolve("acc-1", projectPin: PinA, selectedInstanceId: "i2");

            res.Kind.ShouldBe(InstanceResolutionKind.Resolved);
            res.Instance!.InstanceId.ShouldBe("i1"); // the pinned project's instance, not the sticky one
        }

        // ─────────────────────────── Multi-account isolation matrix ───────────────────────────

        [Fact]
        public void Resolve_IsolatesAccounts_OneAccountNeverResolvesAnothersInstance()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-A", Meta("iA", project: "A", pathHash: HashA), "conn-A");
            reg.Register("acc-B", Meta("iB", project: "B", pathHash: HashB), "conn-B");

            var resA = reg.Resolve("acc-A", null, null);
            var resB = reg.Resolve("acc-B", null, null);

            resA.Instance!.InstanceId.ShouldBe("iA");
            resB.Instance!.InstanceId.ShouldBe("iB");

            // A pin/sticky of one account can never reach the other account's instance.
            reg.Resolve("acc-A", projectPin: PinB, selectedInstanceId: null).Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);
            reg.Resolve("acc-A", null, selectedInstanceId: "iB").Instance!.InstanceId.ShouldBe("iA");
            reg.Resolve("acc-C", null, null).Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);
        }

        [Fact]
        public void GetInstances_UnknownAccount_IsEmpty()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-1", Meta("i1"), "conn-1");
            reg.GetInstances("acc-other").ShouldBeEmpty();
            reg.GetInstances(null).ShouldBeEmpty();
        }

        [Fact]
        public void GetAccountByConnection_IsScopedCorrectly()
        {
            var (reg, _) = NewRegistry();
            reg.Register("acc-A", Meta("iA", pathHash: HashA), "conn-A");
            reg.Register("acc-B", Meta("iB", pathHash: HashB), "conn-B");

            reg.GetAccountByConnection("conn-A").ShouldBe("acc-A");
            reg.GetAccountByConnection("conn-B").ShouldBe("acc-B");
            reg.GetAccountByConnection("conn-unknown").ShouldBeNull();
        }

        // ─────────────────────────── MRU bump ───────────────────────────

        [Fact]
        public void TouchByConnection_BumpsLastActive_ChangesMruWinner()
        {
            var (reg, clock) = NewRegistry();
            reg.Register("acc-1", Meta("i1", project: "GameA", pathHash: HashA), "conn-1");
            clock.Advance(TimeSpan.FromSeconds(5));
            reg.Register("acc-1", Meta("i2", project: "GameB", pathHash: HashB), "conn-2");

            reg.Resolve("acc-1", null, null).Instance!.InstanceId.ShouldBe("i2"); // i2 is MRU

            clock.Advance(TimeSpan.FromSeconds(5));
            reg.TouchByConnection("conn-1"); // i1 becomes most-recently-active

            reg.Resolve("acc-1", null, null).Instance!.InstanceId.ShouldBe("i1");
        }
    }
}
