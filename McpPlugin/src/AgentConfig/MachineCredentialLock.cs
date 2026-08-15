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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The cross-process lock protecting the machine credential store — design 04 §2, identical
    /// semantics in C# (this class) and TS (cli-core twin). Lock file:
    /// <c>~/.ai-game-dev/credentials.lock</c>, a sibling of the data file, never the data file
    /// itself.
    ///
    /// <para><b>Protocol (04 §2):</b></para>
    /// <list type="bullet">
    ///   <item><b>Acquire</b> — exclusive-create <b>only</b> (<see cref="FileMode.CreateNew"/> /
    ///         TS <c>open(…,'wx')</c>), writing <c>{pid, startedAt, hostId}</c>; the handle is
    ///         closed immediately after the write so the mtime is visible to peers. No
    ///         <c>FileShare.None</c>/flock reliance anywhere: .NET skips advisory locks on NFS/SMB
    ///         and <c>File.Delete</c> ignores them on Linux (O6). Failure retries with jittered
    ///         backoff.</item>
    ///   <item><b>Stale takeover (compare-and-delete)</b> — a candidate whose <b>last-write</b>
    ///         time (never last-access) is older than <see cref="LOCK_STALE_MS"/> and whose
    ///         <c>hostId</c> matches the local host: read its content, re-stat, delete only if the
    ///         content is byte-identical to what was read, race for exclusive-create, and verify
    ///         the new lock's content is our own before entering. A lock with a foreign (or
    ///         unparseable) <c>hostId</c> — a network home — is only taken over after
    ///         <see cref="FOREIGN_HOST_STALE_MS"/> (24 h).</item>
    ///   <item><b>Release</b> — delete the lock file (guarded: never deletes a peer's lock — see
    ///         <see cref="MachineCredentialLockHandle.Dispose"/>).</item>
    ///   <item><b>Budget exhausted</b> — the attempt fails as <b>busy</b>
    ///         (<see cref="TryAcquire"/> returns <c>null</c>); it never proceeds lock-free
    ///         (D9 REVISED — the removed "kill-switch" would have reintroduced the reuse
    ///         race).</item>
    /// </list>
    ///
    /// <para><b>Constants are a shared cross-language contract</b>: the names and values match the
    /// TS twin verbatim so the golden-parity suite (task x1) can assert equality. The ordering
    /// <see cref="REFRESH_HTTP_TIMEOUT"/> &lt; <see cref="LOCK_STALE_MS"/> &lt;
    /// <see cref="ACQUIRE_BUDGET"/> is the invariant: a live holder inside one HTTP call can never
    /// be declared stale, and a waiter always outlives the stale threshold so takeover is
    /// reachable.</para>
    ///
    /// <para><b>Windows <c>FileOptions.DeleteOnClose</c> (04 §2 "optional hardening") is
    /// deliberately not used:</b> holding the handle open would conflict with the base protocol's
    /// "close immediately so the mtime is peer-visible" requirement, diverge the observable
    /// semantics from the TS twin (which cannot hold-with-delete-on-close), and change what peers
    /// can read while the lock is held. Crash release is instead provided by the stale-takeover
    /// path, proven by the kill -9 recovery plant (04 §5).</para>
    ///
    /// <para><b>hostId</b> defaults to <see cref="System.Net.Dns.GetHostName"/> — the same
    /// <c>gethostname</c> source Node's <c>os.hostname()</c> uses, so C# and TS processes on one
    /// machine recognise each other as local. Comparison is case-insensitive.</para>
    /// </summary>
    public sealed class MachineCredentialLock
    {
        /// <summary>File name of the lock file, a sibling of the credential data file (04 §2).</summary>
        public const string LockFileName = "credentials.lock";

        // ── Shared cross-language contract constants (04 §2) ─────────────────────────────────────
        // Names and values match the TS twin verbatim (x1 asserts cross-language equality), which
        // is why they are SCREAMING_SNAKE_CASE rather than C# convention. All values are in
        // milliseconds. The ordering REFRESH_HTTP_TIMEOUT < LOCK_STALE_MS < ACQUIRE_BUDGET is the
        // protocol invariant — never edit one without re-checking the other two.

        /// <summary>
        /// Timeout for the refresh HTTP call made inside the critical section, in ms. Must be
        /// explicitly applied to the HttpClient by the refresher (task b3) — .NET's default is
        /// 100 s, which would outlive <see cref="LOCK_STALE_MS"/> and let a live holder be
        /// declared stale.
        /// </summary>
        public const int REFRESH_HTTP_TIMEOUT = 15_000;

        /// <summary>
        /// Age (by last-write time) past which a local-host lock is a stale-takeover candidate, in ms.
        /// </summary>
        public const int LOCK_STALE_MS = 60_000;

        /// <summary>
        /// Total time an acquire attempt keeps trying before failing as "busy", in ms. Strictly
        /// greater than <see cref="LOCK_STALE_MS"/> so a waiter outlives the stale threshold and
        /// takeover is reachable.
        /// </summary>
        public const int ACQUIRE_BUDGET = 75_000;

        /// <summary>
        /// Age past which a lock with a foreign or unparseable <c>hostId</c> may be taken over, in
        /// ms (24 h; 04 §2). Foreign PIDs cannot be probed and cross-host mtimes are clock-skewed,
        /// so the bar is deliberately high. An unparseable lock document has no provable local
        /// owner either, so it gets the same conservative bar.
        /// </summary>
        public const long FOREIGN_HOST_STALE_MS = 24L * 60 * 60 * 1000;

        // Jittered backoff between acquire attempts (04 §2): doubling from Initial to Max, each
        // delay drawn uniformly from [d/2, d]. Internal tuning, not part of the shared contract.
        private const int InitialBackoffMs = 50;
        private const int MaxBackoffMs = 1_000;

        // 0700 (owner rwx) as a binary bit-pattern — C# has no octal literals. Mirrors
        // MachineCredentialStore's directory policy so a lock-first code path (login acquires the
        // lock before the first store write) never leaves ~/.ai-game-dev world-readable.
        private const uint PosixDirectoryPermissions = 0b111_000_000; // rwx --- ---

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false, // compact, like the TS twin's JSON.stringify default
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _baseDirectory;
        private readonly string _lockPath;
        private readonly string _hostId;
        private readonly int _acquireBudgetMs;
        private readonly long _staleMs;
        private readonly long _foreignStaleMs;

        /// <summary>
        /// Create a lock rooted at <paramref name="baseDirectory"/>, or at <c>~/.ai-game-dev</c>
        /// when null (the same root as <see cref="MachineCredentialStore"/>).
        /// <paramref name="hostId"/> overrides the local host identifier — tests only; production
        /// callers use the default.
        /// </summary>
        public MachineCredentialLock(string? baseDirectory = null, string? hostId = null)
            : this(baseDirectory, hostId, ACQUIRE_BUDGET, LOCK_STALE_MS, FOREIGN_HOST_STALE_MS)
        {
        }

        /// <summary>
        /// Test seam (InternalsVisibleTo: McpPlugin.Tests): override the protocol timings so unit
        /// tests of the busy / foreign-bar paths do not consume the real 75 s budget. Production
        /// code paths always go through the public constructor, which pins the contract constants.
        /// </summary>
        internal MachineCredentialLock(
            string? baseDirectory,
            string? hostId,
            int acquireBudgetMs,
            long staleMs,
            long foreignStaleMs)
        {
            _baseDirectory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                MachineCredentialStore.DirectoryName);
            _lockPath = Path.Combine(_baseDirectory, LockFileName);
            _hostId = hostId ?? ResolveLocalHostId();
            _acquireBudgetMs = acquireBudgetMs;
            _staleMs = staleMs;
            _foreignStaleMs = foreignStaleMs;
        }

        /// <summary>Absolute path of the directory holding the lock (and the store).</summary>
        public string BaseDirectory => _baseDirectory;

        /// <summary>Absolute path of the lock file.</summary>
        public string LockPath => _lockPath;

        /// <summary>The host identifier written into (and compared against) lock documents.</summary>
        public string HostId => _hostId;

        /// <summary>
        /// Try to acquire the lock within <see cref="ACQUIRE_BUDGET"/>. Returns the held lock, or
        /// <c>null</c> when the budget is exhausted — the attempt then fails as <b>busy</b> and the
        /// caller must NOT proceed lock-free (04 §2, D9 REVISED). Dispose the handle to release.
        /// </summary>
        public MachineCredentialLockHandle? TryAcquire()
        {
            EnsureBaseDirectory();

            var stopwatch = Stopwatch.StartNew();
            var random = new Random();
            var backoffMs = InitialBackoffMs;

            while (true)
            {
                var handle = TryCreateOwnLock();
                if (handle != null)
                    return handle;

                handle = TryTakeOverStaleLock();
                if (handle != null)
                    return handle;

                var remainingMs = _acquireBudgetMs - stopwatch.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    return null; // busy — never lock-free (04 §2, D9 REVISED)

                // Jittered backoff (04 §2): uniform in [backoff/2, backoff], clamped so the final
                // sleep wakes exactly at the deadline for one last attempt.
                var delayMs = backoffMs / 2 + random.Next(backoffMs / 2 + 1);
                if (delayMs > remainingMs)
                    delayMs = (int)remainingMs;
                Thread.Sleep(delayMs);
                backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
            }
        }

        /// <summary>
        /// Run <paramref name="criticalSection"/> while holding the lock, then release. Returns
        /// <c>false</c> (without running the action) when the acquire budget is exhausted — busy,
        /// never lock-free.
        /// </summary>
        public bool TryRunExclusive(Action criticalSection)
        {
            if (criticalSection == null)
                throw new ArgumentNullException(nameof(criticalSection));

            using (var handle = TryAcquire())
            {
                if (handle == null)
                    return false;
                criticalSection();
                return true;
            }
        }

        /// <summary>
        /// The logout delete path (04 §2, exposed for F6): acquire → unlink store → release →
        /// unlink lock. Server-side revocation is the caller's job and happens before this call.
        /// Returns <c>false</c> (store untouched) when the lock is busy.
        /// </summary>
        public bool TryDeleteStore(MachineCredentialStore store)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            return TryRunExclusive(store.Delete);
        }

        // ── Acquire: exclusive-create only (04 §2) ───────────────────────────────────────────────

        private MachineCredentialLockHandle? TryCreateOwnLock()
        {
            var content = BuildOwnContent();
            try
            {
                // FileMode.CreateNew is the entire mutual-exclusion primitive — atomic on every
                // OS and every filesystem we support, including NFS/SMB (O6). FileShare.Read lets
                // a peer's staleness read proceed during the microseconds the handle is open.
                using (var stream = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(content, 0, content.Length);
                } // closed immediately: the close publishes the mtime to peers (04 §2)
            }
            catch (IOException)
            {
                return null; // the lock exists — somebody else holds it (or a transient failure; retry)
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return new MachineCredentialLockHandle(_lockPath, content);
        }

        // ── Stale takeover: compare-and-delete (04 §2) ───────────────────────────────────────────

        private MachineCredentialLockHandle? TryTakeOverStaleLock()
        {
            if (!File.Exists(_lockPath))
                return null; // vanished — the next loop iteration races exclusive-create

            DateTime firstLastWriteUtc;
            try
            {
                // Last-WRITE time, never last-access (04 §2): atime is unreliable (relatime,
                // noatime mounts) and is touched by the peers' own staleness reads.
                firstLastWriteUtc = File.GetLastWriteTimeUtc(_lockPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }

            var observed = TryReadAllBytes(_lockPath);
            if (observed == null)
                return null; // vanished or unreadable right now — retry later

            var ageMs = (DateTime.UtcNow - firstLastWriteUtc).TotalMilliseconds;
            var isLocal = HostIdMatchesLocal(ParseHostId(observed));
            var thresholdMs = isLocal ? _staleMs : _foreignStaleMs;
            if (ageMs < thresholdMs)
                return null; // a live (or not-provably-dead) holder — keep waiting

            // Compare-and-delete: re-stat, then delete only if the content is still byte-identical
            // to what was read — a candidate that changed under us was re-acquired and is not stale.
            try
            {
                if (File.GetLastWriteTimeUtc(_lockPath) != firstLastWriteUtc)
                    return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }

            var reRead = TryReadAllBytes(_lockPath);
            if (reRead == null || !BytesEqual(observed, reRead))
                return null;

            try
            {
                File.Delete(_lockPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null; // somebody else's delete/create won — retry
            }

            // Race for exclusive-create…
            var handle = TryCreateOwnLock();
            if (handle == null)
                return null; // another waiter won the create race

            // …and verify the new lock's content is our own before entering the critical section
            // (04 §2): a racing waiter's compare-and-delete may have destroyed our fresh lock and
            // replaced it with its own in the window between our create and now.
            if (!handle.IsStillOwned())
            {
                handle.Dispose(); // guarded release: never deletes the usurper's lock
                return null;
            }

            return handle;
        }

        // ── Lock document {pid, startedAt, hostId} (04 §2) ───────────────────────────────────────

        private sealed class LockDocument
        {
            public long Pid { get; set; }
            public string? StartedAt { get; set; }
            public string? HostId { get; set; }
        }

        private byte[] BuildOwnContent()
        {
            var document = new LockDocument
            {
                Pid = CurrentPid,
                StartedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                HostId = _hostId,
            };
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, SerializerOptions));
        }

        private static long CurrentPid
        {
            get
            {
                using (var process = Process.GetCurrentProcess())
                    return process.Id; // netstandard2.1-safe (Environment.ProcessId is net5+)
            }
        }

        private static string? ParseHostId(byte[] content)
        {
            try
            {
                using (var document = JsonDocument.Parse(content))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("hostId", out var hostId)
                        && hostId.ValueKind == JsonValueKind.String)
                        return hostId.GetString();
                }
            }
            catch (JsonException)
            {
                // Unparseable (empty file from a writer killed between create and write, foreign
                // format, corruption) — no provable local owner, treated as foreign (24 h bar).
            }
            return null;
        }

        private bool HostIdMatchesLocal(string? hostId)
            => hostId != null && string.Equals(hostId, _hostId, StringComparison.OrdinalIgnoreCase);

        private static string ResolveLocalHostId()
        {
            try
            {
                // Same gethostname source as Node's os.hostname(), so the TS twin's locks are
                // recognised as local. Environment.MachineName (NetBIOS, upper-cased, 15-char
                // truncated on Windows) would NOT match it.
                return System.Net.Dns.GetHostName();
            }
            catch (Exception)
            {
                return Environment.MachineName;
            }
        }

        // ── Shared low-level helpers (also used by the handle) ───────────────────────────────────

        /// <summary>
        /// Read the lock file without ever blocking a peer's rename/delete
        /// (<c>FileShare.ReadWrite | FileShare.Delete</c>), with a short retry for transient
        /// sharing violations. Returns <c>null</c> when the file is gone or unreadable.
        /// </summary>
        internal static byte[]? TryReadAllBytes(string path)
        {
            const int readAttempts = 3;
            for (var attempt = 1; attempt <= readAttempts; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        return buffer.ToArray();
                    }
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                catch (DirectoryNotFoundException)
                {
                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt == readAttempts)
                        return null;
                    Thread.Sleep(25);
                }
            }
            return null;
        }

        internal static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private void EnsureBaseDirectory()
        {
            Directory.CreateDirectory(_baseDirectory);
            SetPosixDirectoryPermissions(_baseDirectory);
        }

        // Owner-only directory permissions, mirroring MachineCredentialStore's policy (that class
        // is deliberately not modified here — see the b2 task boundary). Same CA1416 pattern.
        private static void SetPosixDirectoryPermissions(string path)
        {
#if NET8_0_OR_GREATER
            if (OperatingSystem.IsWindows())
                return;
            File.SetUnixFileMode(path, (UnixFileMode)PosixDirectoryPermissions);
#else
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
                return;
            _ = chmod(path, PosixDirectoryPermissions);
#endif
        }

#if !NET8_0_OR_GREATER
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int chmod(string path, uint mode);
#endif
    }

    /// <summary>
    /// A held <see cref="MachineCredentialLock"/>. Dispose to release (04 §2: delete the lock
    /// file). The release is guarded — it compares the on-disk document against the bytes this
    /// process wrote and skips the delete when they differ, so a peer's lock is never destroyed
    /// even if ours was (protocol-violatingly) taken over.
    /// </summary>
    public sealed class MachineCredentialLockHandle : IDisposable
    {
        private readonly string _lockPath;
        private readonly byte[] _content;
        private int _disposed;

        internal MachineCredentialLockHandle(string lockPath, byte[] content)
        {
            _lockPath = lockPath;
            _content = content;
        }

        /// <summary>Absolute path of the lock file this handle owns.</summary>
        public string LockPath => _lockPath;

        /// <summary>
        /// True when the on-disk lock document is still byte-identical to what this process
        /// wrote — i.e. the lock has not been (wrongly) taken over. Used by the takeover path's
        /// verify-own-content step (04 §2) and by holders that want to assert their own integrity
        /// after a long critical section.
        /// </summary>
        public bool IsStillOwned()
        {
            var onDisk = MachineCredentialLock.TryReadAllBytes(_lockPath);
            return onDisk != null && MachineCredentialLock.BytesEqual(onDisk, _content);
        }

        /// <summary>Release the lock (04 §2): guarded delete of the lock file. Idempotent.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            var onDisk = MachineCredentialLock.TryReadAllBytes(_lockPath);
            if (onDisk != null && !MachineCredentialLock.BytesEqual(onDisk, _content))
                return; // not ours any more — never delete a peer's lock

            try
            {
                File.Delete(_lockPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort: an undeletable lock file ages into stale takeover (04 §2).
            }
        }
    }
}
