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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// The atomic-write contract of the machine credential store (design 04 §1): temp sibling in
    /// the same directory → fsync → rename, with the Windows rename retry policy (≥4 attempts,
    /// ≥250 ms backoff on EBUSY/EPERM/EACCES-alike holders). A reader must never observe a
    /// truncated document, and a writer killed before the rename must leave the destination intact.
    /// </summary>
    public sealed class MachineCredentialStoreAtomicityTests : IDisposable
    {
        private readonly string _baseDir;

        public MachineCredentialStoreAtomicityTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "agd-store-atomic-" + Guid.NewGuid().ToString("N"), ".ai-game-dev");
        }

        public void Dispose()
        {
            var parent = Path.GetDirectoryName(_baseDir);
            if (parent != null && Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }

        private MachineCredentialStore NewStore() => new MachineCredentialStore(_baseDir);

        private static MachineCredentials Credentials(string accessToken, string refreshToken) => new MachineCredentials
        {
            Families = new MachineCredentialFamilies
            {
                Plugin = new MachineCredentialFamily
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ClientId = "unity-mcp-plugin",
                    Scope = "mcp:plugin",
                },
            },
        };

        [Fact]
        public void RetryPolicy_MeetsTheStoreContract()
        {
            // Part of the v2 store contract (design 04 §1) — not a tunable.
            MachineCredentialStore.RenameRetryAttempts.ShouldBeGreaterThanOrEqualTo(4);
            MachineCredentialStore.RenameRetryDelayMs.ShouldBeGreaterThanOrEqualTo(250);
        }

        [Fact]
        public void StrayTempSibling_FromAWriterKilledBeforeRename_DoesNotAffectReads()
        {
            var store = NewStore();
            store.Write(Credentials("GOOD-AT", "GOOD-RT"));

            // A writer killed between fsync and rename leaves exactly this: a temp sibling with
            // arbitrary content, next to an untouched destination.
            File.WriteAllText(
                Path.Combine(_baseDir, MachineCredentialStore.CredentialsFileName + ".deadbeef.tmp"),
                "partial garbage from a killed writer");

            var result = store.TryRead();
            result.Status.ShouldBe(MachineCredentialStoreStatus.Ok);
            result.Credentials!.Families!.Plugin!.AccessToken.ShouldBe("GOOD-AT");
        }

        /// <summary>
        /// Mandated plant (04 §5, Windows CI leg): the rename retry policy survives a simulated
        /// destination-handle holder (AV scanner / indexer / reader without FILE_SHARE_DELETE).
        /// Without the retry loop the write throws immediately while the handle is held — RED.
        /// The elapsed floor proves a retry actually happened (the first attempt fails at ~0 ms).
        /// </summary>
        [Fact]
        public void Write_RetriesUntilDestinationHandleReleased_Windows()
        {
            if (!OperatingSystem.IsWindows())
                return; // FileShare semantics are only enforced by Windows; POSIX rename ignores holders.

            var store = NewStore();
            store.Write(Credentials("OLD-AT", "OLD-RT"));

            // Hold the destination WITHOUT FileShare.Delete: File.Replace needs delete access on the
            // destination, so every rename attempt fails with a sharing violation until release.
            // The release runs on a DEDICATED thread, not Task.Run: on a saturated 2-core CI
            // runner the thread pool can delay a queued work item far past the 600 ms sleep
            // (observed >1 s: the write exhausted its full retry window AND teardown still found
            // the handle open), which fails the test for a reason the store does not have.
            var holder = new FileStream(store.CredentialsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = new Thread(() =>
            {
                Thread.Sleep(600);
                holder.Dispose();
            });
            release.Start();

            var stopwatch = Stopwatch.StartNew();
            store.Write(Credentials("NEW-AT", "NEW-RT")); // must survive the holder via retries
            stopwatch.Stop();
            release.Join();

            stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(400); // ≥1 backoff happened
            store.Read()!.Families!.Plugin!.AccessToken.ShouldBe("NEW-AT");
        }

        /// <summary>
        /// Mandated plant (04 §5): a reader concurrent with writers never observes a truncated
        /// readable document — it sees the old document or the new one, complete, every time.
        /// Replacing the atomic temp-sibling+rename write with a direct in-place write
        /// (<c>File.WriteAllBytes</c>, the pre-v2 behaviour) turns this RED: readers sample the
        /// truncate-then-write window and decode partial content.
        /// <para>
        /// The READER sets the pace, not the writer. An earlier form ran a fixed 120 writes and
        /// then required ≥ 25 successful reads inside that window — a fixed budget against a
        /// load-dependent cost, which went red on a saturated hosted runner (20 reads) with the
        /// store fully atomic. Now the writer rewrites continuously until the reader has made
        /// its quota of reads, so every read overlaps writing and the quota is always reachable;
        /// the timeout only guards against a hang.
        /// </para>
        /// </summary>
        [Fact]
        public async Task ConcurrentReaders_NeverObserveATruncatedDocument()
        {
            const int requiredReads = 100;
            const int requiredWrites = 20;
            var hangGuard = TimeSpan.FromSeconds(120);
            var store = NewStore();

            // Padding widens the on-disk document so an in-place truncate-then-write window is
            // reliably observable; carried as an unknown field to also exercise preservation.
            using var padding = JsonDocument.Parse(
                JsonSerializer.Serialize(new string('x', 64 * 1024)));

            MachineCredentials Payload(int i)
            {
                var credentials = Credentials("AT-" + i, "RT-" + i);
                credentials.ExtensionData = new Dictionary<string, JsonElement>
                {
                    ["padding"] = padding.RootElement.Clone(),
                };
                return credentials;
            }

            store.Write(Payload(0));

            using var stopWriting = new CancellationTokenSource();
            var writesCompleted = 0;
            var writer = Task.Run(() =>
            {
                for (var i = 1; !stopWriting.IsCancellationRequested; i++)
                {
                    store.Write(Payload(i));
                    Interlocked.Increment(ref writesCompleted);
                }
            });

            var violations = new List<string>();
            var generationsSeen = new HashSet<string>();
            var successfulReads = 0;
            var elapsed = Stopwatch.StartNew();

            // Keep reading until BOTH quotas are met: enough reads to sample the write window,
            // and enough writes that those reads genuinely overlapped rewriting. The writer is
            // still running at every read, so no read here is a quiet-file read.
            while ((successfulReads < requiredReads || Volatile.Read(ref writesCompleted) < requiredWrites)
                   && violations.Count < 5 // enough evidence
                   && !writer.IsCompleted  // a faulted writer ends the run; awaited below
                   && elapsed.Elapsed < hangGuard)
            {
                byte[] raw;
                try
                {
                    // FileShare.ReadWrite | Delete: observe the file without ever blocking the
                    // writer's rename — this reader adds no synchronization of its own.
                    using var streamRead = new FileStream(
                        store.CredentialsPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var buffer = new MemoryStream();
                    streamRead.CopyTo(buffer);
                    raw = buffer.ToArray();
                }
                catch (IOException)
                {
                    continue; // transient open failure during a rename — not a torn read
                }

                try
                {
                    if (raw.Length == 0)
                        throw new InvalidDataException("observed an empty credential file");

                    var json = Encoding.UTF8.GetString(MachineCredentialStore.UnprotectBytes(raw));
                    using var doc = JsonDocument.Parse(json);

                    var plugin = doc.RootElement.GetProperty("families").GetProperty("plugin");
                    var accessToken = plugin.GetProperty("accessToken").GetString()!;
                    var refreshToken = plugin.GetProperty("refreshToken").GetString()!;

                    // Both fields must carry the SAME generation — a mixed pair is a torn document.
                    accessToken.ShouldStartWith("AT-");
                    refreshToken.ShouldBe("RT-" + accessToken.Substring(3));

                    doc.RootElement.GetProperty("padding").GetString()!.Length.ShouldBe(64 * 1024);
                    generationsSeen.Add(accessToken);
                    successfulReads++;
                }
                catch (Exception ex)
                {
                    violations.Add(ex.GetType().Name + ": " + ex.Message);
                }

                Thread.Sleep(1);
            }

            stopWriting.Cancel();
            await writer; // propagate any writer failure

            violations.ShouldBeEmpty();
            // Anti-vacuity: the assertion above proves nothing unless reads actually succeeded
            // while the document was being rewritten. Both quotas are reachable at any load —
            // the loop above only ends early on a violation, a writer fault, or the hang guard.
            successfulReads.ShouldBeGreaterThanOrEqualTo(requiredReads,
                $"the reader must complete its quota within the {hangGuard.TotalSeconds:0}s hang guard");
            writesCompleted.ShouldBeGreaterThanOrEqualTo(requiredWrites,
                "the reads must have overlapped real rewriting");
            generationsSeen.Count.ShouldBeGreaterThan(1,
                "the reader must have observed the document change under it — otherwise it never " +
                "sampled a write at all");
        }

        [Fact]
        public void AtomicWrite_PreservesPosixPermissions_OnCreateAndOnReplace()
        {
            if (OperatingSystem.IsWindows())
                return; // POSIX-only assertion; exercised on the Linux/macOS CI legs. (CA1416 guard)

            var store = NewStore();

            store.Write(Credentials("FIRST-AT", "FIRST-RT")); // create path (File.Move)
            File.GetUnixFileMode(store.CredentialsPath)
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
            File.GetUnixFileMode(store.BaseDirectory)
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // 0700

            store.Write(Credentials("SECOND-AT", "SECOND-RT")); // replace path (File.Replace)
            File.GetUnixFileMode(store.CredentialsPath)
                .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600 survives the rename
        }

        [Fact]
        public void AtomicWrite_LeavesNoTempSiblingBehind_OnSuccess()
        {
            var store = NewStore();
            store.Write(Credentials("AT", "RT"));
            store.Write(Credentials("AT2", "RT2"));

            Directory.GetFiles(_baseDir, "*.tmp").ShouldBeEmpty();
        }
    }
}
