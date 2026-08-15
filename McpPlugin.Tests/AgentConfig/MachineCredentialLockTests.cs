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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The cross-process lock protocol (design 04 §2 + the owner-adopted b2/c2 review amendments)
    /// — in-process halves: the shared cross-language constant contract (pinned against
    /// <c>LockProtocol.GoldenVectors.json</c>), acquire/release document semantics (incl. the
    /// per-acquisition nonce), the busy path (never lock-free, D9 REVISED), the staleness bars
    /// (local 60 s / foreign 24 h / unparseable 60 s per the B2 amendment), the takeover-intent
    /// arbiter (deterministic live-intent backoff + crashed-intent recovery), the B1
    /// own-artifact cleanup, guarded release, and the F6 logout delete path. The three mandated
    /// takeover plants run as REAL subprocesses in
    /// <see cref="MachineCredentialLockTakeoverPlantTests"/>.
    /// Tests that exercise waiting paths use the internal timing-override constructor so they do
    /// not consume the real 75 s budget; the contract constants themselves are pinned below.
    /// </summary>
    public sealed class MachineCredentialLockTests : IDisposable
    {
        private readonly string _baseDir;

        public MachineCredentialLockTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-lock-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private string LockPath => Path.Combine(_baseDir, MachineCredentialLock.LockFileName);
        private string IntentPath => Path.Combine(_baseDir, MachineCredentialLock.TakeoverIntentFileName);

        private MachineCredentialLock NewLock(Action<string>? diagnostics = null) => new(_baseDir, null, diagnostics);

        private MachineCredentialLock NewLock(int budgetMs, string? hostId = null, Action<string>? diagnostics = null) =>
            new(_baseDir, hostId,
                acquireBudgetMs: budgetMs,
                staleMs: MachineCredentialLock.LOCK_STALE_MS,
                foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS,
                diagnostics: diagnostics);

        /// <summary>Well-formed lock document JSON with the given hostId (no nonce — shape only needs pid/startedAt/hostId).</summary>
        private static string WellFormedDocument(string hostId, string pid = "424242") =>
            "{\"pid\":" + pid + ",\"startedAt\":\"2026-01-01T00:00:00.0000000+00:00\",\"hostId\":" +
            JsonSerializer.Serialize(hostId) + "}";

        /// <summary>Plant a protocol file with the given content, aged by <paramref name="age"/>.</summary>
        private void PlantFile(string path, string content, TimeSpan age)
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        }

        private void PlantLock(string hostId, TimeSpan age) => PlantFile(LockPath, WellFormedDocument(hostId), age);

        private static readonly TimeSpan StalePastLocalBar = TimeSpan.FromMilliseconds(MachineCredentialLock.LOCK_STALE_MS * 2);

        // ── Shared cross-language constant contract (04 §2; asserted against TS by x1) ──────────

        [Fact]
        public void Constants_MatchTheSharedCrossLanguageContract()
        {
            // Values are the contract (same names/values in the TS twin) — not tunables.
            MachineCredentialLock.REFRESH_HTTP_TIMEOUT.ShouldBe(15_000);
            MachineCredentialLock.LOCK_STALE_MS.ShouldBe(60_000);
            MachineCredentialLock.ACQUIRE_BUDGET.ShouldBe(75_000);
            MachineCredentialLock.FOREIGN_HOST_STALE_MS.ShouldBe(86_400_000L);
            MachineCredentialLock.LockFileName.ShouldBe("credentials.lock");
            MachineCredentialLock.TakeoverIntentFileName.ShouldBe("credentials.lock.takeover");
        }

        [Fact]
        public void Constants_MatchTheCommittedGoldenVector()
        {
            // The same document the TS twin pins (test/golden-vectors/LockProtocol.GoldenVectors.json
            // in cli-core) — x1 asserts both sides against it, parsed-value comparison (04 §5).
            var path = Path.Combine(AppContext.BaseDirectory, "LockProtocol.GoldenVectors.json");
            File.Exists(path).ShouldBeTrue("golden vector missing from test output: " + path);

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            root.GetProperty("lockFileName").GetString().ShouldBe(MachineCredentialLock.LockFileName);
            root.GetProperty("takeoverIntentFileName").GetString().ShouldBe(MachineCredentialLock.TakeoverIntentFileName);
            root.GetProperty("refreshHttpTimeoutMs").GetInt32().ShouldBe(MachineCredentialLock.REFRESH_HTTP_TIMEOUT);
            root.GetProperty("lockStaleMs").GetInt32().ShouldBe(MachineCredentialLock.LOCK_STALE_MS);
            root.GetProperty("acquireBudgetMs").GetInt32().ShouldBe(MachineCredentialLock.ACQUIRE_BUDGET);
            root.GetProperty("foreignLockStaleMs").GetInt64().ShouldBe(MachineCredentialLock.FOREIGN_HOST_STALE_MS);
        }

        [Fact]
        public void Constants_Ordering_IsTheProtocolInvariant()
        {
            // REFRESH_HTTP_TIMEOUT < LOCK_STALE_MS: a live holder inside one HTTP call can never
            // be declared stale. LOCK_STALE_MS < ACQUIRE_BUDGET: a waiter outlives the stale
            // threshold, so takeover is reachable.
            MachineCredentialLock.REFRESH_HTTP_TIMEOUT.ShouldBeLessThan(MachineCredentialLock.LOCK_STALE_MS);
            MachineCredentialLock.LOCK_STALE_MS.ShouldBeLessThan(MachineCredentialLock.ACQUIRE_BUDGET);
            ((long)MachineCredentialLock.LOCK_STALE_MS).ShouldBeLessThan(MachineCredentialLock.FOREIGN_HOST_STALE_MS);
        }

        [Fact]
        public void LockAndIntent_AreSiblingsOfTheCredentialFile_NeverTheDataFileItself()
        {
            var mutex = NewLock();
            var store = new MachineCredentialStore(_baseDir);

            Path.GetDirectoryName(mutex.LockPath).ShouldBe(Path.GetDirectoryName(store.CredentialsPath));
            Path.GetDirectoryName(mutex.TakeoverIntentPath).ShouldBe(Path.GetDirectoryName(store.CredentialsPath));
            mutex.LockPath.ShouldNotBe(store.CredentialsPath);
            mutex.TakeoverIntentPath.ShouldNotBe(store.CredentialsPath);
            mutex.TakeoverIntentPath.ShouldNotBe(mutex.LockPath);
        }

        // ── Acquire / release document semantics ────────────────────────────────────────────────

        [Fact]
        public void Acquire_WritesExactlyPidStartedAtHostIdNonce_AndReleaseDeletesTheLock()
        {
            var mutex = NewLock();

            using (var handle = mutex.TryAcquire())
            {
                handle.ShouldNotBeNull();
                File.Exists(LockPath).ShouldBeTrue();

                using var document = JsonDocument.Parse(File.ReadAllText(LockPath));
                var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
                names.ShouldBe(new[] { "hostId", "nonce", "pid", "startedAt" }); // the shared doc shape (x1)

                document.RootElement.GetProperty("pid").GetInt64().ShouldBe(Environment.ProcessId);
                document.RootElement.GetProperty("hostId").GetString().ShouldBe(mutex.HostId);
                DateTimeOffset.Parse(document.RootElement.GetProperty("startedAt").GetString()!)
                    .ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
                // Nonce: fresh random 128-bit hex per acquisition attempt.
                Regex.IsMatch(document.RootElement.GetProperty("nonce").GetString()!, "^[0-9a-f]{32}$").ShouldBeTrue();
            }

            File.Exists(LockPath).ShouldBeFalse(); // release = delete the lock file (04 §2)
        }

        [Fact]
        public void Acquire_UsesAFreshNoncePerAcquisition()
        {
            var mutex = NewLock();

            string NonceOfCurrentLock()
            {
                using var document = JsonDocument.Parse(File.ReadAllText(LockPath));
                return document.RootElement.GetProperty("nonce").GetString()!;
            }

            string first;
            using (var handle = mutex.TryAcquire())
            {
                handle.ShouldNotBeNull();
                first = NonceOfCurrentLock();
            }
            using (var handle = mutex.TryAcquire())
            {
                handle.ShouldNotBeNull();
                // Same process, same instance, possibly the same clock tick — the nonce is what
                // keeps the two documents distinct (review A2: byte-identical verify-own must
                // never confuse two attempts).
                NonceOfCurrentLock().ShouldNotBe(first);
            }
        }

        [Fact]
        public void Acquire_ClosesTheHandleImmediately_TheLockFileIsPeerReadableWhileHeld()
        {
            var mutex = NewLock();
            using var handle = mutex.TryAcquire();
            handle.ShouldNotBeNull();

            // No FileShare.None / flock reliance anywhere (O6): while the lock is HELD, a peer
            // must be able to read the document (staleness checks) — the holder keeps no open
            // handle at all.
            var bytes = File.ReadAllBytes(LockPath);
            bytes.Length.ShouldBeGreaterThan(0);
        }

        [Fact]
        public void Parser_ToleratesUnknownFields_InTheLockDocument()
        {
            // A future twin may extend the document; extra fields must not demote a local doc to
            // the foreign bar. Planted: well-formed + local hostId + unknown fields, stale.
            PlantFile(LockPath,
                "{\"pid\":1,\"startedAt\":\"2026-01-01T00:00:00Z\",\"hostId\":" +
                JsonSerializer.Serialize(NewLock().HostId) + ",\"nonce\":\"aa\",\"futureField\":123}",
                StalePastLocalBar);

            using var handle = NewLock(budgetMs: 10_000).TryAcquire();
            handle.ShouldNotBeNull(); // local bar applied despite unknown fields
        }

        // ── Busy: budget exhaustion is a failure, never lock-free (04 §2, D9 REVISED) ───────────

        [Fact]
        public void Acquire_AgainstALiveLocalHolder_FailsBusy_AndNeverTouchesTheLock()
        {
            var mutex = NewLock();
            using var held = mutex.TryAcquire();
            held.ShouldNotBeNull();

            var originalBytes = File.ReadAllBytes(LockPath);
            var originalMtime = File.GetLastWriteTimeUtc(LockPath);

            var waiter = NewLock(budgetMs: 1_200);
            var stopwatch = Stopwatch.StartNew();
            var result = waiter.TryAcquire();
            stopwatch.Stop();

            result.ShouldBeNull(); // busy — never lock-free
            stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(1_200); // kept trying the whole budget
            File.ReadAllBytes(LockPath).ShouldBe(originalBytes); // holder's lock byte-identical
            File.GetLastWriteTimeUtc(LockPath).ShouldBe(originalMtime); // …and untouched
        }

        // ── Staleness bars: local 60 s / foreign 24 h / unparseable 60 s (B2 amendment) ─────────

        [Fact]
        public void StaleLocalLock_IsTakenOver_AndTheIntentIsCleanedUp()
        {
            var mutex = NewLock(budgetMs: 10_000);
            PlantLock(mutex.HostId, StalePastLocalBar);

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull(); // stale local candidate → intent-serialized takeover
            handle.IsStillOwned().ShouldBeTrue(); // the on-disk lock is OUR document now
            using var document = JsonDocument.Parse(File.ReadAllText(LockPath));
            document.RootElement.GetProperty("pid").GetInt64().ShouldBe(Environment.ProcessId);
            File.Exists(IntentPath).ShouldBeFalse(); // the arbiter releases its intent on every path
        }

        [Fact]
        public void HostIdComparison_IsCaseInsensitive()
        {
            var mutex = NewLock(budgetMs: 10_000);
            // A TS peer's os.hostname() may differ from ours only by case — still LOCAL.
            PlantLock(mutex.HostId.ToUpperInvariant(), StalePastLocalBar);

            using var handle = mutex.TryAcquire();
            handle.ShouldNotBeNull();
        }

        [Fact]
        public void ForeignHostLock_PastTheLocalStaleBar_IsNotTakenOver()
        {
            var mutex = NewLock(budgetMs: 1_200);
            PlantLock("some-other-host.example", StalePastLocalBar);
            var originalBytes = File.ReadAllBytes(LockPath);

            mutex.TryAcquire().ShouldBeNull(); // foreign hostId (network home) → 24 h bar → busy
            File.ReadAllBytes(LockPath).ShouldBe(originalBytes);
        }

        [Fact]
        public void ForeignHostLock_OlderThan24Hours_IsTakenOver()
        {
            var mutex = NewLock(budgetMs: 10_000);
            PlantLock("some-other-host.example", TimeSpan.FromHours(25));

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull();
            handle.IsStillOwned().ShouldBeTrue();
        }

        [Fact]
        public void EmptyOrMissingHostId_InAWellFormedDocument_ComparesAsForeign()
        {
            // Its writer COMPLETED a write (valid JSON), so the torn-write argument does not
            // apply — apply the conservative 24 h bar (contract item 7).
            PlantFile(LockPath, WellFormedDocument(hostId: ""), StalePastLocalBar);
            NewLock(budgetMs: 1_200).TryAcquire().ShouldBeNull();

            // Valid JSON of the wrong shape (missing hostId entirely): same class.
            PlantFile(LockPath, "{\"pid\":1}", StalePastLocalBar);
            NewLock(budgetMs: 1_200).TryAcquire().ShouldBeNull();
            File.ReadAllText(LockPath).ShouldBe("{\"pid\":1}");
        }

        [Fact]
        public void UnparseableLock_IsStaleEligibleAtTheLocalBar_AndEmitsOneDiagnostic()
        {
            // OWNER-ADOPTED AMENDMENT (review B2, flipping the original 24 h pin): a torn
            // document's writer provably never completed its acquire write, which precedes
            // entering the critical section — so taking it over can never collide with an
            // in-flight refresh, and the 24 h bar would only turn every crash-in-the-create-window
            // artifact into a day-long machine-wide auth freeze.
            var warnings = new List<string>();
            PlantFile(LockPath, "not json at all", TimeSpan.FromMinutes(5));

            using var handle = NewLock(budgetMs: 10_000, diagnostics: warnings.Add).TryAcquire();

            handle.ShouldNotBeNull(); // 60 s bar, not 24 h
            handle.IsStillOwned().ShouldBeTrue();
            warnings.Count(w => w.Contains("unparseable")).ShouldBe(1); // exactly one diagnostic
        }

        [Fact]
        public void ZeroByteLock_IsStaleEligibleAtTheLocalBar()
        {
            // The B1 crash shape itself: a writer killed between create and write.
            PlantFile(LockPath, "", TimeSpan.FromMinutes(5));

            using var handle = NewLock(budgetMs: 10_000).TryAcquire();
            handle.ShouldNotBeNull();
        }

        [Fact]
        public void FreshUnparseableLock_IsNotTakenOverBeforeTheLocalBar()
        {
            // The amendment moves the bar to 60 s — NOT to zero: a fresh torn artifact still
            // waits out LOCK_STALE_MS (its writer may be frozen mid-create, the accepted residual).
            PlantFile(LockPath, "not json at all", TimeSpan.FromSeconds(1));

            NewLock(budgetMs: 1_200).TryAcquire().ShouldBeNull();
            File.ReadAllText(LockPath).ShouldBe("not json at all");
        }

        // ── The takeover-intent arbiter ─────────────────────────────────────────────────────────

        [Fact]
        public void StaleTakeover_BacksOff_WhileALiveTakeoverIntentExists()
        {
            // DETERMINISTIC arbiter test (mandated): a stale lock is takeover-eligible, but a
            // LIVE intent file proves another claimant is mid-takeover — this waiter must back
            // off and fail busy, touching neither artifact. REDs when the arbiter is bypassed
            // (a waiter that ignores the intent takes the stale lock and acquires).
            var mutex = NewLock(budgetMs: 1_500);
            PlantLock(mutex.HostId, StalePastLocalBar);
            PlantFile(IntentPath, WellFormedDocument(mutex.HostId, pid: "999999"), TimeSpan.Zero); // live claimant
            var lockBytes = File.ReadAllBytes(LockPath);
            var intentBytes = File.ReadAllBytes(IntentPath);

            mutex.TryAcquire().ShouldBeNull(); // backed off — busy, never a bypass

            File.ReadAllBytes(LockPath).ShouldBe(lockBytes);     // stale lock untouched
            File.ReadAllBytes(IntentPath).ShouldBe(intentBytes); // live intent untouched
        }

        [Fact]
        public void CrashedIntent_IsRecovered_ByTheSameStalenessRules()
        {
            // A claimant that died between intent-create and intent-release leaves its intent
            // behind; it is recovered by the same staleness + compare-and-delete rules, after
            // which the stale lock itself is taken over.
            var mutex = NewLock(budgetMs: 10_000);
            PlantLock(mutex.HostId, StalePastLocalBar);
            PlantFile(IntentPath, WellFormedDocument(mutex.HostId, pid: "999999"), StalePastLocalBar); // crashed claimant

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull();
            handle.IsStillOwned().ShouldBeTrue();
            File.Exists(IntentPath).ShouldBeFalse(); // recovered, used, and released
        }

        // ── B1: own-artifact cleanup on write failure after a successful exclusive-create ───────

        [Fact]
        public void CreateWriteFailure_NeverLeavesAPoisonedLockArtifact()
        {
            // Review B1: CreateNew succeeded ⇒ this process provably owns the file; a write
            // failure (disk full, AV) must best-effort delete it. Without the cleanup, the empty
            // file survives as a poisoned artifact every peer must wait out.
            var mutex = NewLock(budgetMs: 800);
            mutex.WriteFaultInjection = () => throw new IOException("disk full (injected)");

            mutex.TryAcquire().ShouldBeNull(); // every attempt faults → busy
            File.Exists(LockPath).ShouldBeFalse(); // no zero-byte lock survives (RED without B1)
        }

        [Fact]
        public void CreateWriteFailure_IsTransient_TheNextAttemptAcquires()
        {
            // One-shot fault: the first create's write fails and is cleaned up; the retry inside
            // the same budget succeeds with a complete document.
            var mutex = NewLock(budgetMs: 10_000);
            var faults = 0;
            mutex.WriteFaultInjection = () =>
            {
                if (faults++ == 0)
                    throw new IOException("transient (injected)");
            };

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull();
            faults.ShouldBeGreaterThanOrEqualTo(2); // the fault DID fire on attempt 1 (anti-vacuity)
            handle.IsStillOwned().ShouldBeTrue();   // final document is complete and ours
        }

        [Fact]
        public void IntentWriteFailure_NeverLeavesAPoisonedIntentArtifact()
        {
            // Same B1 control at the second create site: a poisoned empty intent would block all
            // stale takeovers until it ages out.
            var mutex = NewLock(budgetMs: 1_000);
            PlantLock(mutex.HostId, StalePastLocalBar); // forces the takeover path (lock create fails first)
            mutex.WriteFaultInjection = () => throw new IOException("disk full (injected)");

            mutex.TryAcquire().ShouldBeNull(); // intent creation faults every time → busy
            File.Exists(IntentPath).ShouldBeFalse(); // no zero-byte intent survives (RED without B1)
            File.Exists(LockPath).ShouldBeTrue();    // and the stale lock was never deleted without an intent
        }

        // ── Guarded release ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Release_NeverDeletesAPeersLock()
        {
            var mutex = NewLock();
            var handle = mutex.TryAcquire();
            handle.ShouldNotBeNull();

            // Simulate a (protocol-violating) usurper replacing our lock while we hold it.
            File.WriteAllText(LockPath, "{\"pid\":1,\"startedAt\":\"x\",\"hostId\":\"usurper\"}");

            handle.IsStillOwned().ShouldBeFalse();
            handle.Dispose();

            File.Exists(LockPath).ShouldBeTrue(); // the usurper's lock survives our release
            File.ReadAllText(LockPath).ShouldContain("usurper");
        }

        [Fact]
        public void Release_IsIdempotent()
        {
            var mutex = NewLock();
            var handle = mutex.TryAcquire();
            handle.ShouldNotBeNull();

            handle.Dispose();
            handle.Dispose(); // second dispose is a no-op, not an error

            File.Exists(LockPath).ShouldBeFalse();
        }

        // ── TryRunExclusive + the F6 logout delete path (04 §2) ─────────────────────────────────

        [Fact]
        public void TryRunExclusive_RunsTheActionUnderTheLock_ThenReleases()
        {
            var mutex = NewLock();
            var ranUnderLock = false;

            var entered = mutex.TryRunExclusive(() =>
            {
                File.Exists(LockPath).ShouldBeTrue(); // the lock is held while the action runs
                ranUnderLock = true;
            });

            entered.ShouldBeTrue();
            ranUnderLock.ShouldBeTrue();
            File.Exists(LockPath).ShouldBeFalse();
        }

        [Fact]
        public void TryRunExclusive_WhenBusy_DoesNotRunTheAction()
        {
            var mutex = NewLock();
            using var held = mutex.TryAcquire();
            held.ShouldNotBeNull();

            var ran = false;
            NewLock(budgetMs: 800).TryRunExclusive(() => ran = true).ShouldBeFalse();
            ran.ShouldBeFalse(); // busy means FAIL — never proceed lock-free (D9 REVISED)
        }

        [Fact]
        public void TryDeleteStore_AcquiresUnlinksStoreReleasesUnlinksLock()
        {
            var store = new MachineCredentialStore(_baseDir);
            store.Write(new MachineCredentials
            {
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily { AccessToken = "AT", RefreshToken = "RT" },
                },
            });

            var mutex = NewLock();
            mutex.TryDeleteStore(store).ShouldBeTrue();

            File.Exists(store.CredentialsPath).ShouldBeFalse(); // store unlinked under the lock
            File.Exists(LockPath).ShouldBeFalse();              // …and the lock unlinked after release
        }

        [Fact]
        public void TryDeleteStore_WhenBusy_LeavesTheStoreIntact()
        {
            var store = new MachineCredentialStore(_baseDir);
            store.Write(new MachineCredentials
            {
                Families = new MachineCredentialFamilies
                {
                    Plugin = new MachineCredentialFamily { AccessToken = "AT", RefreshToken = "RT" },
                },
            });
            PlantLock("some-other-host.example", TimeSpan.Zero); // live foreign holder

            NewLock(budgetMs: 800).TryDeleteStore(store).ShouldBeFalse();

            File.Exists(store.CredentialsPath).ShouldBeTrue(); // busy logout deletes NOTHING
            File.Exists(LockPath).ShouldBeTrue();
        }
    }
}
