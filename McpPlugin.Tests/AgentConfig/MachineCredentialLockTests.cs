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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The cross-process lock protocol (design 04 §2) — in-process halves: the shared
    /// cross-language constant contract, acquire/release document semantics, the busy path
    /// (never lock-free, D9 REVISED), the local-vs-foreign staleness bar, guarded release, and
    /// the F6 logout delete path. The three mandated takeover plants run as REAL subprocesses in
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

        private MachineCredentialLock NewLock() => new(_baseDir);

        private MachineCredentialLock NewLock(int budgetMs, string? hostId = null) =>
            new(_baseDir, hostId,
                acquireBudgetMs: budgetMs,
                staleMs: MachineCredentialLock.LOCK_STALE_MS,
                foreignStaleMs: MachineCredentialLock.FOREIGN_HOST_STALE_MS);

        /// <summary>Plant a lock file with the given hostId, aged by <paramref name="age"/>.</summary>
        private void PlantLock(string hostId, TimeSpan age, string pid = "424242")
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(LockPath,
                "{\"pid\":" + pid + ",\"startedAt\":\"2026-01-01T00:00:00.0000000+00:00\",\"hostId\":" +
                JsonSerializer.Serialize(hostId) + "}");
            File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow - age);
        }

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
        public void LockFile_IsASiblingOfTheCredentialFile_NeverTheDataFileItself()
        {
            var mutex = NewLock();
            var store = new MachineCredentialStore(_baseDir);

            Path.GetDirectoryName(mutex.LockPath).ShouldBe(Path.GetDirectoryName(store.CredentialsPath));
            mutex.LockPath.ShouldNotBe(store.CredentialsPath);
        }

        // ── Acquire / release document semantics ────────────────────────────────────────────────

        [Fact]
        public void Acquire_WritesPidStartedAtHostId_AndReleaseDeletesTheLock()
        {
            var mutex = NewLock();

            using (var handle = mutex.TryAcquire())
            {
                handle.ShouldNotBeNull();
                File.Exists(LockPath).ShouldBeTrue();

                using var document = JsonDocument.Parse(File.ReadAllText(LockPath));
                document.RootElement.GetProperty("pid").GetInt64()
                    .ShouldBe(Environment.ProcessId);
                document.RootElement.GetProperty("hostId").GetString().ShouldBe(mutex.HostId);
                DateTimeOffset.Parse(document.RootElement.GetProperty("startedAt").GetString()!)
                    .ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
            }

            File.Exists(LockPath).ShouldBeFalse(); // release = delete the lock file (04 §2)
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

        // ── Staleness: local 60 s bar, foreign/unparseable 24 h bar (04 §2) ─────────────────────

        [Fact]
        public void StaleLocalLock_IsTakenOver_AndTheNewLockIsOurOwn()
        {
            var mutex = NewLock(budgetMs: 10_000);
            PlantLock(mutex.HostId, age: TimeSpan.FromMilliseconds(MachineCredentialLock.LOCK_STALE_MS * 2));

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull(); // stale local candidate → compare-and-delete takeover
            handle.IsStillOwned().ShouldBeTrue(); // the on-disk lock is OUR document now
            using var document = JsonDocument.Parse(File.ReadAllText(LockPath));
            document.RootElement.GetProperty("pid").GetInt64().ShouldBe(Environment.ProcessId);
        }

        [Fact]
        public void ForeignHostLock_PastTheLocalStaleBar_IsNotTakenOver()
        {
            var mutex = NewLock(budgetMs: 1_200);
            PlantLock("some-other-host.example", age: TimeSpan.FromMilliseconds(MachineCredentialLock.LOCK_STALE_MS * 2));
            var originalBytes = File.ReadAllBytes(LockPath);

            mutex.TryAcquire().ShouldBeNull(); // foreign hostId (network home) → 24 h bar → busy
            File.ReadAllBytes(LockPath).ShouldBe(originalBytes);
        }

        [Fact]
        public void ForeignHostLock_OlderThan24Hours_IsTakenOver()
        {
            var mutex = NewLock(budgetMs: 10_000);
            PlantLock("some-other-host.example", age: TimeSpan.FromHours(25));

            using var handle = mutex.TryAcquire();

            handle.ShouldNotBeNull();
            handle.IsStillOwned().ShouldBeTrue();
        }

        [Fact]
        public void UnparseableLock_IsTreatedAsForeign_NotTakenOverAtTheLocalBar()
        {
            // A writer killed between create and write leaves an empty/garbage document with no
            // provable local owner — conservative 24 h bar, never the 60 s local bar.
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(LockPath, "not json at all");
            File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow - TimeSpan.FromMinutes(5));

            var mutex = NewLock(budgetMs: 1_200);
            mutex.TryAcquire().ShouldBeNull();
            File.ReadAllText(LockPath).ShouldBe("not json at all");
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
            PlantLock("some-other-host.example", age: TimeSpan.Zero); // live foreign holder

            NewLock(budgetMs: 800).TryDeleteStore(store).ShouldBeFalse();

            File.Exists(store.CredentialsPath).ShouldBeTrue(); // busy logout deletes NOTHING
            File.Exists(LockPath).ShouldBeTrue();
        }
    }
}
