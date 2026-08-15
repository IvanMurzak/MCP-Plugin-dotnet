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
using System.Threading;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The three mandated stale-takeover plants (design 04 §5), run with REAL OS subprocesses —
    /// not threads — and the REAL contract constants (public constructor; no timing overrides):
    /// <list type="number">
    ///   <item>kill -9 the holder mid-refresh → survivors recover after
    ///         <see cref="MachineCredentialLock.LOCK_STALE_MS"/> within
    ///         <see cref="MachineCredentialLock.ACQUIRE_BUDGET"/>;</item>
    ///   <item>a holder sleeping <see cref="MachineCredentialLock.REFRESH_HTTP_TIMEOUT"/> inside
    ///         the lock is NOT taken over;</item>
    ///   <item>two waiters observing the same stale lock never both acquire
    ///         (compare-and-delete).</item>
    /// </list>
    /// The subprocess is <c>McpPlugin.Tests.LockHarness</c>, spawned via <c>dotnet</c>. These
    /// tests are wall-clock slow BY DESIGN (the first waits through the real 60 s staleness
    /// threshold): scaling the constants down would test a different protocol than the one that
    /// ships.
    /// </summary>
    public sealed class MachineCredentialLockTakeoverPlantTests : IDisposable
    {
        private readonly string _baseDir;

        public MachineCredentialLockTakeoverPlantTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-lock-plant-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            try
            {
                if (parent != null && Directory.Exists(parent))
                    Directory.Delete(parent, recursive: true);
            }
            catch (IOException)
            {
                // A straggler subprocess may still hold a file for a moment; temp dirs are
                // OS-cleaned eventually. Never fail a plant on teardown.
            }
        }

        private string LockPath => Path.Combine(_baseDir, MachineCredentialLock.LockFileName);

        // ── Plant 1: kill -9 recovery ───────────────────────────────────────────────────────────

        /// <summary>
        /// Mandated plant (04 §5): kill -9 the holder mid-refresh; a survivor recovers after
        /// <c>LOCK_STALE_MS</c> within <c>ACQUIRE_BUDGET</c>. RED when the stale-takeover path is
        /// removed (the survivor stays busy for the full budget and exits 2). The lower time
        /// bound proves the takeover was not premature; the budget bound proves it is reachable —
        /// the ordering invariant, live.
        /// </summary>
        [Fact]
        public void KillNineHolder_SurvivorRecoversAfterStaleThreshold_WithinBudget()
        {
            using var holder = HarnessProcess.Start("hold", _baseDir, 600_000);
            holder.WaitForLine("ACQUIRED", TimeSpan.FromSeconds(60));

            var lockMtimeUtc = File.GetLastWriteTimeUtc(LockPath);
            holder.Kill(); // TerminateProcess / SIGKILL: no finally blocks, no release — a crash
            File.Exists(LockPath).ShouldBeTrue("the killed holder must leave its lock file behind");

            using var survivor = HarnessProcess.Start("acquire", _baseDir, 100);
            survivor.WaitForExit(TimeSpan.FromMilliseconds(MachineCredentialLock.ACQUIRE_BUDGET + 45_000));

            survivor.ExitCode.ShouldBe(0, "survivor output: " + survivor.Describe());
            var acquiredLine = survivor.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal));

            // Not premature: the survivor entered only once the dead holder's lock had aged past
            // LOCK_STALE_MS (2 s tolerance for filesystem timestamp granularity).
            var acquiredAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(ParseT(acquiredLine)).UtcDateTime;
            (acquiredAtUtc - lockMtimeUtc).TotalMilliseconds
                .ShouldBeGreaterThanOrEqualTo(MachineCredentialLock.LOCK_STALE_MS - 2_000);

            // Within budget: recovery is reachable (LOCK_STALE_MS < ACQUIRE_BUDGET).
            ParseElapsed(acquiredLine).ShouldBeLessThanOrEqualTo(MachineCredentialLock.ACQUIRE_BUDGET);

            File.Exists(LockPath).ShouldBeFalse("the survivor completed its critical section and released");
        }

        // ── Plant 2: a live holder inside one HTTP call is never declared stale ─────────────────

        /// <summary>
        /// Mandated plant (04 §5): a holder sleeping <c>REFRESH_HTTP_TIMEOUT</c> (the longest a
        /// refresh HTTP call may take) inside the lock is NOT taken over. The holder itself
        /// proves it — after the sleep it re-reads the lock and reports <c>OWNED=true</c> only if
        /// the document is still byte-identical to its own. RED when the ordering invariant
        /// <c>REFRESH_HTTP_TIMEOUT &lt; LOCK_STALE_MS</c> is broken (a waiter steals the lock
        /// mid-hold and the holder exits 3 with <c>OWNED=false</c>).
        /// </summary>
        [Fact]
        public void HolderSleepingRefreshHttpTimeout_IsNotTakenOver()
        {
            using var holder = HarnessProcess.Start("hold", _baseDir, MachineCredentialLock.REFRESH_HTTP_TIMEOUT);
            var holderAcquired = holder.WaitForLine("ACQUIRED", TimeSpan.FromSeconds(60));

            // The waiter runs CONCURRENTLY with the ~15 s hold and must simply wait it out.
            using var waiter = HarnessProcess.Start("acquire", _baseDir, 100);

            holder.WaitForExit(TimeSpan.FromSeconds(90));
            waiter.WaitForExit(TimeSpan.FromSeconds(120));

            holder.ExitCode.ShouldBe(0, "holder output: " + holder.Describe());
            holder.Lines.Any(l => l.StartsWith("OWNED=true", StringComparison.Ordinal))
                .ShouldBeTrue("the holder must hold its own lock through the whole simulated refresh: " + holder.Describe());

            waiter.ExitCode.ShouldBe(0, "waiter output: " + waiter.Describe());
            var waiterAcquired = waiter.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal));
            var holderReleasedAt = ParseT(holder.Lines.First(l => l.StartsWith("RELEASED", StringComparison.Ordinal)));

            // The waiter entered only AFTER the holder released — never by takeover (100 ms
            // tolerance for cross-process wall-clock reads).
            ParseT(waiterAcquired).ShouldBeGreaterThanOrEqualTo(holderReleasedAt - 100);

            // Anti-vacuity: the waiter genuinely overlapped the hold (it spent most of the 15 s
            // window waiting) — a waiter that started after the release would prove nothing.
            ParseElapsed(waiterAcquired).ShouldBeGreaterThanOrEqualTo(8_000);
            ParseT(waiterAcquired).ShouldBeGreaterThanOrEqualTo(ParseT(holderAcquired));
        }

        // ── Plant 3: two waiters on the SAME stale lock — compare-and-delete ────────────────────

        /// <summary>
        /// Mandated plant (04 §5): two waiters observing the same stale lock must not both
        /// acquire. Both are held at a start barrier and released together so they race the SAME
        /// candidate (spawn jitter would otherwise serialise them trivially). Exclusive entry is
        /// proven by an exclusive-create sentinel inside the critical section (<c>OVERLAP</c>
        /// when violated) plus the sequential-timing assertion. Three rounds, because the
        /// interleaving that compare-and-delete + verify-own-content exists to stop (waiter B
        /// deleting waiter A's FRESH lock straight after A's create) is scheduling-dependent —
        /// removing the guards turns this RED via OVERLAP within a few rounds.
        /// </summary>
        [Fact]
        public void TwoWaitersOnTheSameStaleLock_NeverBothAcquire()
        {
            const int rounds = 3;
            const int csHoldMs = 1_500;
            var localHostId = new MachineCredentialLock(_baseDir).HostId;

            for (var round = 1; round <= rounds; round++)
            {
                var roundDir = Path.Combine(_baseDir, "round-" + round);
                Directory.CreateDirectory(roundDir);
                var lockPath = Path.Combine(roundDir, MachineCredentialLock.LockFileName);

                // The shared stale candidate: local hostId (so the 60 s bar applies), aged well
                // past LOCK_STALE_MS, from a pid that no longer exists.
                File.WriteAllText(lockPath,
                    "{\"pid\":424242,\"startedAt\":\"2026-01-01T00:00:00.0000000+00:00\",\"hostId\":" +
                    JsonSerializer.Serialize(localHostId) + "}");
                File.SetLastWriteTimeUtc(lockPath,
                    DateTime.UtcNow - TimeSpan.FromMilliseconds(MachineCredentialLock.LOCK_STALE_MS + 30_000));

                using var waiterA = HarnessProcess.Start("acquire", roundDir, csHoldMs, waitGo: true);
                using var waiterB = HarnessProcess.Start("acquire", roundDir, csHoldMs, waitGo: true);
                waiterA.WaitForLine("READY", TimeSpan.FromSeconds(60));
                waiterB.WaitForLine("READY", TimeSpan.FromSeconds(60));
                File.WriteAllText(Path.Combine(roundDir, "go"), "go"); // release the barrier

                waiterA.WaitForExit(TimeSpan.FromSeconds(150));
                waiterB.WaitForExit(TimeSpan.FromSeconds(150));

                var context = "round " + round + "; A: " + waiterA.Describe() + "; B: " + waiterB.Describe();

                // Mutual exclusion, directly: nobody ever found the critical-section sentinel taken.
                waiterA.Lines.Any(l => l.StartsWith("OVERLAP", StringComparison.Ordinal)).ShouldBeFalse(context);
                waiterB.Lines.Any(l => l.StartsWith("OVERLAP", StringComparison.Ordinal)).ShouldBeFalse(context);
                waiterA.ExitCode.ShouldBe(0, context);
                waiterB.ExitCode.ShouldBe(0, context);

                // Sequential, not simultaneous: the second entry began only after the first
                // waiter's full 1.5 s critical section (100 ms cross-process clock tolerance).
                var tA = ParseT(waiterA.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal)));
                var tB = ParseT(waiterB.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal)));
                Math.Abs(tA - tB).ShouldBeGreaterThanOrEqualTo(csHoldMs - 100, context);

                // Anti-vacuity: the FIRST entrant got in fast, via the stale-takeover path — not
                // by both waiters timing into some slower route.
                var firstElapsed = Math.Min(
                    ParseElapsed(waiterA.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal))),
                    ParseElapsed(waiterB.Lines.First(l => l.StartsWith("ACQUIRED", StringComparison.Ordinal))));
                firstElapsed.ShouldBeLessThan(15_000, context);
            }
        }

        // ── Harness plumbing ────────────────────────────────────────────────────────────────────

        private static long ParseT(string line)
        {
            var match = Regex.Match(line, @"\bt=(\d+)");
            match.Success.ShouldBeTrue("no t= in line: " + line);
            return long.Parse(match.Groups[1].Value);
        }

        private static long ParseElapsed(string line)
        {
            var match = Regex.Match(line, @"\belapsed=(\d+)");
            match.Success.ShouldBeTrue("no elapsed= in line: " + line);
            return long.Parse(match.Groups[1].Value);
        }

        /// <summary>
        /// A running lock-harness subprocess (<c>dotnet McpPlugin.Tests.LockHarness.dll …</c>)
        /// with line-buffered stdout capture the test can wait on in real time.
        /// </summary>
        private sealed class HarnessProcess : IDisposable
        {
            private readonly Process _process;
            private readonly List<string> _lines = new();
            private readonly StringBuilder _stderr = new();
            private readonly object _gate = new();

            public static HarnessProcess Start(string command, string baseDir, int milliseconds, bool waitGo = false)
            {
                var harnessDll = Path.Combine(AppContext.BaseDirectory, "McpPlugin.Tests.LockHarness.dll");
                File.Exists(harnessDll).ShouldBeTrue(
                    "lock harness not found at " + harnessDll + " — is the McpPlugin.Tests.LockHarness ProjectReference intact?");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                };
                startInfo.ArgumentList.Add(harnessDll);
                startInfo.ArgumentList.Add(command);
                startInfo.ArgumentList.Add(baseDir);
                startInfo.ArgumentList.Add(milliseconds.ToString());
                if (waitGo)
                    startInfo.ArgumentList.Add("--wait-go");

                return new HarnessProcess(startInfo);
            }

            private HarnessProcess(ProcessStartInfo startInfo)
            {
                _process = new Process { StartInfo = startInfo };
                _process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null)
                        return;
                    lock (_gate)
                    {
                        _lines.Add(e.Data);
                        Monitor.PulseAll(_gate);
                    }
                };
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                        _stderr.AppendLine(e.Data);
                };
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            public IReadOnlyList<string> Lines
            {
                get
                {
                    lock (_gate)
                        return _lines.ToArray();
                }
            }

            public int ExitCode => _process.ExitCode;

            public string Describe()
            {
                lock (_gate)
                    return "stdout=[" + string.Join(" | ", _lines) + "] stderr=[" + _stderr.ToString().Trim() + "]";
            }

            public string WaitForLine(string prefix, TimeSpan timeout)
            {
                var deadlineUtc = DateTime.UtcNow + timeout;
                lock (_gate)
                {
                    while (true)
                    {
                        var match = _lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
                        if (match != null)
                            return match;
                        var remaining = deadlineUtc - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                            throw new TimeoutException(
                                "no '" + prefix + "' line within " + timeout + "; " + DescribeUnlocked());
                        Monitor.Wait(_gate, remaining);
                    }
                }
            }

            private string DescribeUnlocked() =>
                "stdout=[" + string.Join(" | ", _lines) + "] stderr=[" + _stderr.ToString().Trim() + "]";

            public void Kill()
            {
                _process.Kill();
                _process.WaitForExit();
            }

            public void WaitForExit(TimeSpan timeout)
            {
                _process.WaitForExit((int)timeout.TotalMilliseconds)
                    .ShouldBeTrue("harness did not exit within " + timeout + "; " + Describe());
                _process.WaitForExit(); // flush the async output readers (documented overload quirk)
            }

            public void Dispose()
            {
                try
                {
                    if (!_process.HasExited)
                        _process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                _process.Dispose();
            }
        }
    }
}
