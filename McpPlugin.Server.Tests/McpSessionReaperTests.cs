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
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The replace-by-identity ALGEBRA: which connection displaces which, and — more important — which
    /// connections must never be able to touch each other.
    ///
    /// <para>These drive <see cref="McpSessionReaper"/> against a recording fake evictor, so every
    /// assertion is about the rule itself rather than about a live Kestrel host. The termination MECHANISM
    /// (that the SDK really releases the displaced session) cannot be proven here by construction and is
    /// proven separately over a real host in <c>McpReplaceByIdentityOverHttpTests</c>. Both are needed: a
    /// fake evictor could report a replace that never terminated anything, and a live host is too coarse
    /// to enumerate the key algebra.</para>
    /// </summary>
    public sealed class McpSessionReaperTests
    {
        const string AccountA = "acct-aaaa";
        const string AccountB = "acct-bbbb";
        const string InstanceX = "0123456789abcdef0123456789abcdef";
        const string InstanceY = "fedcba9876543210fedcba9876543210";

        /// <summary>
        /// Records every eviction request so a test can assert on WHAT was evicted, not just how many.
        ///
        /// <para><b>The recording is locked, and it has to be.</b> <see cref="McpSessionReaper"/> calls
        /// <see cref="Evict"/> OUTSIDE its own claim lock — deliberately, so a claim never holds a lock
        /// across foreign code — so concurrent claims reach this method concurrently. An unsynchronised
        /// <see cref="List{T}"/> here lost and duplicated entries under
        /// <see cref="ConcurrentClaimsForOneIdentity_ConvergeOnASingleSlot"/>, producing a flake that read
        /// as a race in the production code and was not one.</para>
        /// </summary>
        sealed class RecordingEvictor : IMcpSdkSessionEvictor
        {
            readonly object _lock = new object();
            readonly List<string> _evicted = new List<string>();

            public bool IsSupported => true;
            public string BindingDiagnostic => "fake";

            /// <summary>A stable snapshot, ordered as the evictions arrived.</summary>
            public IReadOnlyList<string> Evicted
            {
                get { lock (_lock) { return _evicted.ToArray(); } }
            }

            public SdkEvictionHandle Evict(string mcpSessionId)
            {
                lock (_lock) { _evicted.Add(mcpSessionId); }
                return new SdkEvictionHandle(SdkEvictionOutcome.Evicted, Task.CompletedTask);
            }
        }

        static (McpSessionReaper Reaper, RecordingEvictor Evictor) Build()
        {
            var evictor = new RecordingEvictor();
            return (new McpSessionReaper(evictor), evictor);
        }

        // ───────────────────────────── the positive control (DoD 4) ─────────────────────────────

        [Fact]
        public void SameAccountSameInstance_SecondConnectionReplacesTheFirst()
        {
            // This is the REQUIRED positive control. Without it, "clients that send no identity are
            // unaffected" would be satisfied by a rule that never replaces anything, and the whole change
            // would score green while doing nothing at all.
            var (reaper, evictor) = Build();

            var first = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            first.ShouldNotBeNull();
            evictor.Evicted.ShouldBeEmpty("a first connection has no predecessor to displace");
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-1");

            var second = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-2");
            second.ShouldNotBeNull();

            // Both halves: the OLD session was the one evicted, and the NEW one holds the slot.
            evictor.Evicted.ShouldBe(new[] { "session-1" });
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-2");
            reaper.TrackedIdentityCount.ShouldBe(1, "a replace must not leave two slots for one identity");

            first!.Displaced.ShouldBeTrue("the displaced session must be able to tell that its shutdown is orderly");
            second!.Displaced.ShouldBeFalse();
            second.DisplacedPredecessor.ShouldNotBeNull();
            second.DisplacedPredecessor!.Outcome.ShouldBe(SdkEvictionOutcome.Evicted);
        }

        // ───────────────────────────── cross-account isolation (DoD 2) ─────────────────────────────

        [Fact]
        public void TwoDifferentAccountsSendingTheSameInstanceId_DoNotAffectEachOther()
        {
            // The security property. The instance id is client-supplied and therefore spoofable; the
            // account comes from the validated credential. So a spoofed instance id must be able to evict
            // only the spoofer's OWN sessions. If this ever fails, any authenticated user can terminate any
            // other user's MCP session by guessing nothing at all.
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-a").ShouldNotBeNull();
            reaper.Claim(AccountB, InstanceX, projectPin: null, mcpSessionId: "session-b").ShouldNotBeNull();

            evictor.Evicted.ShouldBeEmpty("one account's identity slot is not the other's, so nothing is displaced");
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-a");
            reaper.CurrentSessionIdFor(AccountB, InstanceX, null).ShouldBe("session-b");
            reaper.TrackedIdentityCount.ShouldBe(2);

            // And a third connection on account B replaces only B's session, never A's.
            reaper.Claim(AccountB, InstanceX, projectPin: null, mcpSessionId: "session-b2").ShouldNotBeNull();
            evictor.Evicted.ShouldBe(new[] { "session-b" });
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-a",
                "account A's session must survive account B's replace");
        }

        [Fact]
        public void SameAccountDifferentInstances_DoNotAffectEachOther()
        {
            // One account legitimately runs several installations (desktop + laptop). They must stack.
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-x");
            reaper.Claim(AccountA, InstanceY, projectPin: null, mcpSessionId: "session-y");

            evictor.Evicted.ShouldBeEmpty();
            reaper.TrackedIdentityCount.ShouldBe(2);
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-x");
            reaper.CurrentSessionIdFor(AccountA, InstanceY, null).ShouldBe("session-y");
        }

        // ───────────────────────────── the pin component (07 §2.5 / T25 / SG-14) ─────────────────────

        [Fact]
        public void SameAccountAndInstanceButDifferentProjectPins_DoNotAffectEachOther()
        {
            // 07-security.md's copied-installation row: a home directory cloned by a VM image or a
            // OneDrive sync genuinely SHARES one installId, so two clones working on different projects
            // would otherwise evict each other forever — a permanent mutual-eviction flap that looks like a
            // network fault to both users. Including the pin in the key is what prevents it.
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: "aaa111", mcpSessionId: "session-p1");
            reaper.Claim(AccountA, InstanceX, projectPin: "bbb222", mcpSessionId: "session-p2");

            evictor.Evicted.ShouldBeEmpty("different projects are different slots");
            reaper.TrackedIdentityCount.ShouldBe(2);

            // Same project ⇒ still replaces. Without this half the test above would be satisfied by a rule
            // that never replaces anything once a pin is present.
            reaper.Claim(AccountA, InstanceX, projectPin: "aaa111", mcpSessionId: "session-p1b");
            evictor.Evicted.ShouldBe(new[] { "session-p1" });
            reaper.CurrentSessionIdFor(AccountA, InstanceX, "bbb222").ShouldBe("session-p2",
                "the other project's session must be untouched");
        }

        [Fact]
        public void UnpinnedAndPinned_AreDifferentSlots()
        {
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-unpinned");
            reaper.Claim(AccountA, InstanceX, projectPin: "aaa111", mcpSessionId: "session-pinned");

            evictor.Evicted.ShouldBeEmpty();
            reaper.TrackedIdentityCount.ShouldBe(2);
        }

        // ───────────────────────────── the compatibility path (DoD 3) ─────────────────────────────

        [Fact]
        public void NoInstanceId_ClaimsNothing_ReplacesNothing_AndDoesNotThrow()
        {
            // Every currently released client lands here. It must be a total no-op — no slot, no eviction,
            // no exception — so the container can be deployed with every client still on the old behaviour.
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, null, projectPin: null, mcpSessionId: "session-1").ShouldBeNull();
            reaper.Claim(AccountA, string.Empty, projectPin: null, mcpSessionId: "session-2").ShouldBeNull();

            evictor.Evicted.ShouldBeEmpty();
            reaper.TrackedIdentityCount.ShouldBe(0);
        }

        [Fact]
        public void NoAccount_ClaimsNothing_EvenWhenAnInstanceIdIsPresent()
        {
            // no-auth / local-token modes resolve no account. An instance id alone must NOT create a slot:
            // an account-less key would pool unrelated clients together and each new connection would evict
            // a stranger's session — a bug that would look exactly like the feature working.
            var (reaper, evictor) = Build();

            reaper.Claim(null, InstanceX, projectPin: null, mcpSessionId: "session-1").ShouldBeNull();
            reaper.Claim(string.Empty, InstanceX, projectPin: null, mcpSessionId: "session-2").ShouldBeNull();

            evictor.Evicted.ShouldBeEmpty();
            reaper.TrackedIdentityCount.ShouldBe(0);
        }

        [Fact]
        public void NoSessionId_ClaimsNothing()
        {
            var (reaper, evictor) = Build();
            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: string.Empty).ShouldBeNull();
            evictor.Evicted.ShouldBeEmpty();
            reaper.TrackedIdentityCount.ShouldBe(0);
        }

        [Fact]
        public void CurrentSessionIdFor_WithNoIdentity_IsNullRatherThanAnEmptyKeyLookup()
        {
            var (reaper, _) = Build();
            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            reaper.CurrentSessionIdFor(null, InstanceX, null).ShouldBeNull();
            reaper.CurrentSessionIdFor(AccountA, null, null).ShouldBeNull();
        }

        // ───────────────────────────── lifecycle / retention ─────────────────────────────

        [Fact]
        public void DisposingTheLease_ReleasesTheSlot_SoNothingOutlivesItsSession()
        {
            // This is the retention guarantee, asserted rather than asserted-in-prose: the stored HMAC
            // lives exactly as long as the session that claimed it. The transport layer disposes this lease
            // in the same `finally` that removes the session's tracker row.
            var (reaper, _) = Build();

            var lease = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            reaper.TrackedIdentityCount.ShouldBe(1);

            lease!.Dispose();

            reaper.TrackedIdentityCount.ShouldBe(0, "the identity must not outlive the session it keys");
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBeNull();
        }

        [Fact]
        public void DisposingTheLeaseTwice_IsIdempotent()
        {
            var (reaper, _) = Build();
            var lease = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            lease!.Dispose();
            lease.Dispose();
            reaper.TrackedIdentityCount.ShouldBe(0);
        }

        [Fact]
        public void ADisplacedLeaseDisposingLate_DoesNotEvictItsOwnSuccessor()
        {
            // THE subtle one, and the reason Release is a compare-and-remove rather than a plain
            // TryRemove(key). A displaced session unwinds AFTER its successor has already installed itself,
            // and its `finally` disposes its lease. An unconditional removal would delete the SUCCESSOR's
            // slot — leaving a live session that no later connection could ever find, and therefore never
            // replace. The leak would be back, reachable only through the code added to fix it, and every
            // other test in this file would still pass.
            var (reaper, evictor) = Build();

            var first = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            var second = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-2");

            // The displaced session finishes unwinding only now — after the successor is in place.
            first!.Dispose();

            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-2",
                "the successor's slot must survive its predecessor's late teardown");
            reaper.TrackedIdentityCount.ShouldBe(1);

            // And the successor is still replaceable — proving the slot is genuinely live, not merely present.
            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-3");
            evictor.Evicted.ShouldBe(new[] { "session-1", "session-2" });
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-3");

            second!.Displaced.ShouldBeTrue();
        }

        [Fact]
        public void SameSessionReClaimingItsOwnSlot_DoesNotEvictItself()
        {
            // A re-entrant claim must not terminate the very session asking to be kept.
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");
            var again = reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-1");

            evictor.Evicted.ShouldBeEmpty("a session must never evict itself");
            again!.Displaced.ShouldBeFalse();
            again.DisplacedPredecessor.ShouldBeNull();
            reaper.CurrentSessionIdFor(AccountA, InstanceX, null).ShouldBe("session-1");
            reaper.TrackedIdentityCount.ShouldBe(1);
        }

        [Fact]
        public void AccountAndInstanceComparison_IsOrdinal_NotCultureOrCaseInsensitive()
        {
            // Account ids and hex tokens are opaque byte strings. A culture-sensitive or case-insensitive
            // comparison would merge two distinct identities into one slot, making one client evict
            // another's session. (Header VALUES are lower-cased at validation time, so a case difference
            // reaching this far means the caller bypassed validation — which must not silently merge.)
            var (reaper, evictor) = Build();

            reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: "session-lower");
            reaper.Claim(AccountA.ToUpperInvariant(), InstanceX, projectPin: null, mcpSessionId: "session-upper");

            evictor.Evicted.ShouldBeEmpty("different byte strings are different accounts");
            reaper.TrackedIdentityCount.ShouldBe(2);
        }

        [Fact]
        public void ConcurrentClaimsForOneIdentity_ConvergeOnASingleSlot()
        {
            // Two initialize requests for one identity can genuinely race. The invariant that matters is
            // that they converge: exactly one slot survives, and every session except the survivor was
            // evicted — so no session is left live-but-unreferenced, which no future replace could reach.
            var (reaper, evictor) = Build();
            const int count = 32;

            Parallel.For(0, count, i => reaper.Claim(AccountA, InstanceX, projectPin: null, mcpSessionId: $"session-{i}"));

            reaper.TrackedIdentityCount.ShouldBe(1);
            var survivor = reaper.CurrentSessionIdFor(AccountA, InstanceX, null);
            survivor.ShouldNotBeNull();

            evictor.Evicted.Count.ShouldBe(count - 1,
                "every session except the surviving one must have been evicted; a smaller number means a " +
                "session was stranded live with nothing referencing it");
            evictor.Evicted.ShouldNotContain(survivor!, "the surviving session must not have been evicted");
        }
    }
}
