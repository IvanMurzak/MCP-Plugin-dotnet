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
using System.Diagnostics;
using System.IO;
using System.Threading;
using com.IvanMurzak.McpPlugin.AgentConfig;

namespace com.IvanMurzak.McpPlugin.Tests.LockHarness
{
    /// <summary>
    /// Subprocess harness for the machine-credential-lock takeover plants (design 04 §5 — real OS
    /// processes, not threads). Spawned by <c>MachineCredentialLockTakeoverPlantTests</c>. Every
    /// protocol-relevant event is reported as a machine-parseable stdout line stamped with a
    /// wall-clock unix-ms timestamp, flushed immediately so the parent can react (e.g. kill -9 a
    /// holder the moment it reports <c>ACQUIRED</c>).
    ///
    /// <para>Commands (all use the PUBLIC lock constructor — real contract constants):</para>
    /// <list type="bullet">
    ///   <item><c>hold &lt;baseDir&gt; &lt;holdMs&gt;</c> — acquire, report, sleep
    ///         <c>holdMs</c> inside the critical section, verify the lock is still our own
    ///         (<c>OWNED=…</c>), release. Exit 0 = held throughout; 2 = busy; 3 = stolen.</item>
    ///   <item><c>acquire &lt;baseDir&gt; &lt;csHoldMs&gt; [--wait-go]</c> — acquire within the
    ///         budget, prove exclusive entry via an exclusive-create sentinel
    ///         (<c>cs.sentinel</c>), hold the critical section <c>csHoldMs</c>, release. Exit
    ///         0 = clean exclusive run; 2 = busy; 4 = OVERLAP (a second holder was inside the
    ///         critical section — mutual-exclusion violation). With <c>--wait-go</c> the process
    ///         reports <c>READY</c> and spins until <c>go</c> appears in <c>baseDir</c> before
    ///         acquiring — a start barrier so two waiters observe the SAME stale lock instead of
    ///         being serialised by process-spawn jitter.</item>
    /// </list>
    /// </summary>
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitBusy = 2;
        private const int ExitStolen = 3;
        private const int ExitOverlap = 4;

        /// <summary>Sentinel proving exclusive critical-section entry in the two-waiter plant.</summary>
        private const string SentinelFileName = "cs.sentinel";

        private static long NowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static void Report(string line)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush(); // the parent test reacts to lines in real time — never buffer
        }

        private static int Main(string[] args)
        {
            if (args.Length < 3 || args.Length > 4)
            {
                Console.Error.WriteLine("usage: (hold|acquire) <baseDir> <milliseconds> [--wait-go]");
                return ExitUsage;
            }

            var command = args[0];
            var baseDir = args[1];
            var milliseconds = int.Parse(args[2]);
            var waitGo = args.Length == 4 && args[3] == "--wait-go";

            switch (command)
            {
                case "hold":
                    return Hold(baseDir, milliseconds);
                case "acquire":
                    return Acquire(baseDir, milliseconds, waitGo);
                default:
                    Console.Error.WriteLine("unknown command: " + command);
                    return ExitUsage;
            }
        }

        /// <summary>
        /// Start barrier: report <c>READY</c>, then spin until the parent creates
        /// <c>&lt;baseDir&gt;/go</c>. Lets the parent line up N waiters and release them within a
        /// few milliseconds of each other.
        /// </summary>
        private static void AwaitGoSignal(string baseDir)
        {
            Report("READY t=" + NowUnixMs);
            var goPath = Path.Combine(baseDir, "go");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (!File.Exists(goPath))
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("go signal never arrived at " + goPath);
                Thread.Sleep(5);
            }
        }

        private static int Hold(string baseDir, int holdMs)
        {
            var mutex = new MachineCredentialLock(baseDir);
            var stopwatch = Stopwatch.StartNew();
            var handle = mutex.TryAcquire();
            if (handle == null)
            {
                Report("BUSY elapsed=" + stopwatch.ElapsedMilliseconds);
                return ExitBusy;
            }

            Report("ACQUIRED t=" + NowUnixMs + " elapsed=" + stopwatch.ElapsedMilliseconds + " pid=" + Environment.ProcessId);

            Thread.Sleep(holdMs); // the "refresh in flight" window — a kill -9 lands here

            var owned = handle.IsStillOwned();
            Report("OWNED=" + (owned ? "true" : "false") + " t=" + NowUnixMs);

            handle.Dispose();
            Report("RELEASED t=" + NowUnixMs);
            return owned ? ExitOk : ExitStolen;
        }

        private static int Acquire(string baseDir, int csHoldMs, bool waitGo)
        {
            if (waitGo)
                AwaitGoSignal(baseDir);

            var mutex = new MachineCredentialLock(baseDir);
            var stopwatch = Stopwatch.StartNew();
            var handle = mutex.TryAcquire();
            if (handle == null)
            {
                Report("BUSY elapsed=" + stopwatch.ElapsedMilliseconds);
                return ExitBusy;
            }

            var acquiredAt = NowUnixMs;
            Report("ACQUIRED t=" + acquiredAt + " elapsed=" + stopwatch.ElapsedMilliseconds + " pid=" + Environment.ProcessId);

            // Exclusive-create sentinel: if a second process is inside the critical section at the
            // same time, its CreateNew fails and it reports OVERLAP — the direct, cross-process
            // proof of the mutual-exclusion property the two-waiter plant asserts.
            var sentinelPath = Path.Combine(baseDir, SentinelFileName);
            try
            {
                using (new FileStream(sentinelPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                }
            }
            catch (IOException)
            {
                Report("OVERLAP t=" + NowUnixMs);
                handle.Dispose();
                return ExitOverlap;
            }

            Thread.Sleep(csHoldMs); // widen the critical-section window so an overlap is observable

            File.Delete(sentinelPath);
            handle.Dispose();
            Report("DONE t=" + NowUnixMs);
            return ExitOk;
        }
    }
}
