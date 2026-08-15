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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The cross-process lock protecting the machine credential store — design 04 §2, identical
    /// semantics in C# (this class) and TS (cli-core <c>credential-lock.ts</c>, task c2). Lock
    /// file: <c>~/.ai-game-dev/credentials.lock</c>, a sibling of the data file, never the data
    /// file itself. The shared contract is pinned by
    /// <c>LockProtocol.GoldenVectors.json</c> (asserted by both languages via x1).
    ///
    /// <para><b>Protocol (04 §2, with the owner-adopted amendments from the b2/c2 review
    /// round):</b></para>
    /// <list type="bullet">
    ///   <item><b>Acquire</b> — exclusive-create <b>only</b> (<see cref="FileMode.CreateNew"/> /
    ///         TS <c>open(…,'wx')</c>), writing <c>{pid, startedAt, hostId, nonce}</c>; the handle
    ///         is closed immediately after the write so the mtime is visible to peers, then the
    ///         file is read back to verify it still carries OUR bytes before we consider
    ///         ourselves the holder. No <c>FileShare.None</c>/flock reliance anywhere: .NET skips
    ///         advisory locks on NFS/SMB and <c>File.Delete</c> ignores them on Linux (O6).
    ///         Failure retries with jittered backoff. The <c>nonce</c> (fresh random 128-bit hex
    ///         per acquisition attempt) makes every document unique, so byte-identical
    ///         verify-own can never confuse two attempts — even two in the same process within
    ///         one clock tick (review A2).</item>
    ///   <item><b>Own-artifact cleanup (review B1)</b> — if the exclusive-create succeeded but
    ///         the content write failed (disk full, AV), the empty file this process provably
    ///         created is best-effort deleted before backing off, for the lock AND the intent
    ///         file. A poisoned zero-byte artifact must never outlive its creator's attempt.</item>
    ///   <item><b>Stale takeover, SERIALIZED through a takeover-intent file</b> — a candidate
    ///         whose <b>last-write</b> time (never last-access) is older than its staleness bar
    ///         may be removed, but only by the claimant that first wins an exclusive-create of
    ///         the sibling intent file (<see cref="TakeoverIntentFileName"/>), reads it back to
    ///         verify ownership, re-validates the candidate is the SAME artifact it judged stale
    ///         (mtime + size + byte-identical content), re-verifies intent ownership immediately
    ///         before the delete, and only then unlinks it — then releases the intent and races
    ///         exclusive-create for the lock itself. The intent arbiter is LOAD-BEARING: without
    ///         it, two lockstep waiters both validate the same unchanged stale file and the
    ///         slower one's delete lands on a freshly re-created LIVE lock (measured
    ///         double-acquiring in the TS twin's 4-process hammer, 3/25 lockstep rounds). A live
    ///         intent (younger than its staleness bar) makes claimants back off; a crashed
    ///         claimant's intent is itself recovered by the same staleness +
    ///         compare-and-delete rules (deliberately unserialized — the residual needs the rare
    ///         crashed-intent precondition ON TOP of the tight race window; accepted by design in
    ///         both languages).</item>
    ///   <item><b>Staleness classes</b> — one shared classifier (mirror of the TS twin's
    ///         <c>classifyLockDocument</c>), applied to the lock AND the intent file:
    ///         <c>local</c> (JSON object whose <c>hostId</c> equals ours,
    ///         <see cref="StringComparison.OrdinalIgnoreCase"/> — the pinned cross-language
    ///         semantic; TS folds via <c>toLowerCase()</c>, diverging only on non-ASCII
    ///         hostnames in the conservative direction) → <see cref="LOCK_STALE_MS"/> (60 s);
    ///         <c>foreign</c> (JSON object with a missing, empty, or different <c>hostId</c> —
    ///         network home / unknown writer) → <see cref="FOREIGN_HOST_STALE_MS"/> (24 h);
    ///         <c>unparseable</c> (zero-byte, corrupted, or any non-object JSON — owner-adopted
    ///         amendment, review B2) → <see cref="LOCK_STALE_MS"/> (60 s), because such an
    ///         artifact's writer provably never completed its acquire write, which precedes
    ///         handle-return, which precedes the critical section — taking it over can never
    ///         collide with an in-flight refresh, and the 24 h bar would only convert every
    ///         crash-in-the-create-window artifact into a day-long machine-wide auth freeze.
    ///         One diagnostic warning is emitted after an unparseable LOCK artifact is removed;
    ///         intent recovery is intentionally silent (mirrored asymmetry).</item>
    ///   <item><b>Release</b> — delete the lock file, guarded: the on-disk bytes are compared to
    ///         our own first, so a peer's lock is never deleted
    ///         (<see cref="MachineCredentialLockHandle.Dispose"/>).</item>
    ///   <item><b>Budget exhausted</b> — the attempt fails as <b>busy</b>
    ///         (<see cref="TryAcquire"/> returns <c>null</c>); it never proceeds lock-free
    ///         (D9 REVISED — the removed "kill-switch" would have reintroduced the reuse
    ///         race).</item>
    ///   <item><b>Windows delete-pending is contention</b> — an exclusive-create racing a
    ///         concurrent unlink can land in the file's DELETE-PENDING window, surfacing as
    ///         <see cref="UnauthorizedAccessException"/> / EPERM-alike <see cref="IOException"/>
    ///         rather than "already exists". Both create sites (lock and intent) treat every such
    ///         failure as transient contention and back off — never a hard failure. (POSIX never
    ///         takes this branch for contention.)</item>
    /// </list>
    ///
    /// <para><b>Constants are a shared cross-language contract</b>: the names and values match the
    /// TS twin verbatim and both sides assert them against
    /// <c>LockProtocol.GoldenVectors.json</c> (x1). The ordering
    /// <see cref="REFRESH_HTTP_TIMEOUT"/> &lt; <see cref="LOCK_STALE_MS"/> &lt;
    /// <see cref="ACQUIRE_BUDGET"/> is the invariant: a live holder inside one HTTP call can never
    /// be declared stale, and a waiter always outlives the stale threshold so takeover is
    /// reachable.</para>
    ///
    /// <para><b>Windows <c>FileOptions.DeleteOnClose</c> (04 §2 "optional hardening") is
    /// deliberately not used:</b> holding the handle open would conflict with the base protocol's
    /// "close immediately so the mtime is peer-visible" requirement and diverge the observable
    /// semantics from the TS twin. Crash release is the stale-takeover path, proven by the
    /// kill -9 recovery plant (04 §5).</para>
    ///
    /// <para><b>hostId</b> defaults to <see cref="System.Net.Dns.GetHostName"/> — the same
    /// <c>gethostname</c> source as Node's <c>os.hostname()</c>, so C# and TS processes on one
    /// machine recognise each other as local. Comparison is ordinal case-insensitive; an empty
    /// or missing <c>hostId</c> always compares as foreign.</para>
    ///
    /// <para><b>Clock caveats (spec-inherent, shared with the TS twin):</b> a backward clock jump
    /// only delays takeover (safe direction); a forward step &gt; ~45 s can declare a live holder
    /// prematurely stale (double refresh — absorbed by the server's D10 grace window); on an
    /// NFS-hosted home the mtime is server-clock while ages are judged against local UtcNow, so a
    /// &gt;60 s skew mis-ages fresh locks. Also note <see cref="File.GetLastWriteTimeUtc(string)"/>
    /// returns the 1601 FILETIME-zero sentinel for a vanished file instead of throwing — every
    /// consumer below pairs the stat with a content read whose null result aborts the path.</para>
    /// </summary>
    public sealed class MachineCredentialLock
    {
        /// <summary>File name of the lock file, a sibling of the credential data file (04 §2).</summary>
        public const string LockFileName = "credentials.lock";

        /// <summary>
        /// File name of the takeover-intent file (sibling of the lock). Removing a stale
        /// <see cref="LockFileName"/> requires FIRST winning an exclusive-create of this file —
        /// the atomic arbiter that serializes concurrent stale-takeover claimants. Part of the
        /// shared cross-language protocol surface (TS: <c>CREDENTIALS_LOCK_TAKEOVER_FILE_NAME</c>);
        /// a C#/TS fleet disagreeing here degrades to the measured double-acquire race.
        /// </summary>
        public const string TakeoverIntentFileName = "credentials.lock.takeover";

        // ── Shared cross-language contract constants (04 §2) ─────────────────────────────────────
        // Names and values match the TS twin verbatim and are pinned by
        // LockProtocol.GoldenVectors.json (x1 asserts cross-language equality), which is why they
        // are SCREAMING_SNAKE_CASE rather than C# convention. All values are in milliseconds. The
        // ordering REFRESH_HTTP_TIMEOUT < LOCK_STALE_MS < ACQUIRE_BUDGET is the protocol
        // invariant — never edit one without re-checking the other two.

        /// <summary>
        /// Timeout for the refresh HTTP call made inside the critical section, in ms. Must be
        /// explicitly applied to the HttpClient by the refresher (task b3) — .NET's default is
        /// 100 s, which would outlive <see cref="LOCK_STALE_MS"/> and let a live holder be
        /// declared stale.
        /// </summary>
        public const int REFRESH_HTTP_TIMEOUT = 15_000;

        /// <summary>
        /// Age (by last-write time) past which a local-host — or unparseable/zero-byte (B2
        /// amendment) — lock is a stale-takeover candidate, in ms.
        /// </summary>
        public const int LOCK_STALE_MS = 60_000;

        /// <summary>
        /// Total time an acquire attempt keeps trying before failing as "busy", in ms. Strictly
        /// greater than <see cref="LOCK_STALE_MS"/> so a waiter outlives the stale threshold and
        /// takeover is reachable.
        /// </summary>
        public const int ACQUIRE_BUDGET = 75_000;

        /// <summary>
        /// Age past which a lock whose well-formed document carries a foreign, empty, or missing
        /// <c>hostId</c> may be taken over, in ms (24 h; 04 §2). Foreign PIDs cannot be probed and
        /// cross-host mtimes are clock-skewed, so the bar is deliberately high. (An unparseable
        /// document is NOT in this class — see <see cref="LOCK_STALE_MS"/> and the B2 amendment.)
        /// </summary>
        public const long FOREIGN_HOST_STALE_MS = 24L * 60 * 60 * 1000;

        // Jittered backoff between acquire attempts (04 §2), mirroring the TS twin: base 25 ms
        // doubled per attempt, capped at 500 ms, then jittered ×[0.5, 1.5). Internal tuning, not
        // part of the shared contract.
        private const int BackoffBaseMs = 25;
        private const int BackoffCapMs = 500;

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
        private readonly string _intentPath;
        private readonly string _hostId;
        private readonly int _acquireBudgetMs;
        private readonly long _staleMs;
        private readonly long _foreignStaleMs;
        private readonly Action<string>? _diagnostics;

        /// <summary>
        /// Test seam (InternalsVisibleTo: McpPlugin.Tests): when set, invoked inside the create
        /// window of BOTH create sites (lock and intent) right after a successful exclusive-create
        /// and before the content write — throwing from it simulates a write failure (disk full,
        /// AV) so the B1 own-artifact cleanup path can be exercised deterministically. Never set
        /// in production.
        /// </summary>
        internal Action? WriteFaultInjection;

        /// <summary>
        /// Test seam: invoked between the intent file's successful exclusive-create and its
        /// read-back verification — lets a test displace the intent there, deterministically
        /// pinning the read-back control (whose deletion is otherwise invisible: the pre-removal
        /// re-verify downstream masks it). Never set in production.
        /// </summary>
        internal Action? AfterIntentCreateInjection;

        /// <summary>
        /// Test seam: invoked right after intent acquisition succeeds, before the candidate
        /// re-validation and the pre-removal intent re-verify — lets a test displace (or restore)
        /// the intent there, deterministically pinning the pre-removal re-verify control. Never
        /// set in production.
        /// </summary>
        internal Action? AfterIntentAcquiredInjection;

        /// <summary>
        /// Create a lock rooted at <paramref name="baseDirectory"/>, or at <c>~/.ai-game-dev</c>
        /// when null (the same root as <see cref="MachineCredentialStore"/>).
        /// <paramref name="hostId"/> overrides the local host identifier — tests only; production
        /// callers use the default. <paramref name="diagnostics"/> receives rare protocol
        /// warnings (e.g. taking over an unparseable lock); when null they go to
        /// <see cref="Trace.TraceWarning(string)"/>.
        /// </summary>
        public MachineCredentialLock(string? baseDirectory = null, string? hostId = null, Action<string>? diagnostics = null)
            : this(baseDirectory, hostId, ACQUIRE_BUDGET, LOCK_STALE_MS, FOREIGN_HOST_STALE_MS, diagnostics)
        {
        }

        /// <summary>
        /// Test seam (InternalsVisibleTo: McpPlugin.Tests): override the protocol timings so unit
        /// tests of the busy / staleness paths do not consume the real 75 s budget. Production
        /// code paths always go through the public constructor, which pins the contract constants.
        /// </summary>
        internal MachineCredentialLock(
            string? baseDirectory,
            string? hostId,
            int acquireBudgetMs,
            long staleMs,
            long foreignStaleMs,
            Action<string>? diagnostics = null)
        {
            _baseDirectory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                MachineCredentialStore.DirectoryName);
            _lockPath = Path.Combine(_baseDirectory, LockFileName);
            _intentPath = Path.Combine(_baseDirectory, TakeoverIntentFileName);
            _hostId = hostId ?? ResolveLocalHostId();
            _acquireBudgetMs = acquireBudgetMs;
            _staleMs = staleMs;
            _foreignStaleMs = foreignStaleMs;
            _diagnostics = diagnostics;
        }

        /// <summary>Absolute path of the directory holding the lock (and the store).</summary>
        public string BaseDirectory => _baseDirectory;

        /// <summary>Absolute path of the lock file.</summary>
        public string LockPath => _lockPath;

        /// <summary>Absolute path of the takeover-intent file (<c>credentials.lock.takeover</c>).</summary>
        public string TakeoverIntentPath => _intentPath;

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
            var attempt = 0;

            while (true)
            {
                var handle = TryCreateOwnLock();
                if (handle != null)
                    return handle;

                if (TryRemoveStaleLock())
                    continue; // stale lock removed — race exclusive-create immediately, no backoff

                var remainingMs = _acquireBudgetMs - stopwatch.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    return null; // busy — never lock-free (04 §2, D9 REVISED)

                var delayMs = NextBackoffMs(random, attempt);
                if (delayMs > remainingMs)
                    delayMs = (int)remainingMs; // wake exactly at the deadline for one last attempt
                Thread.Sleep(delayMs);
                attempt++;
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
            var content = BuildOwnDocument();
            if (!TryExclusiveCreateWithContent(_lockPath, content))
                return null;

            // Verify the new lock's content is our own before considering ourselves the holder
            // (04 §2): a (protocol-violating or crashed-intent-residual) racer may have replaced
            // our fresh lock in the window after the close. Never touch a mismatching file.
            var readBack = TryReadAllBytes(_lockPath);
            if (readBack == null || !BytesEqual(readBack, content))
                return null;

            return new MachineCredentialLockHandle(_lockPath, content);
        }

        /// <summary>
        /// One exclusive-create attempt writing <paramref name="content"/> and closing the handle
        /// immediately (the close publishes the mtime to peers, 04 §2). Returns false on ANY
        /// create failure: "already exists" AND the Windows delete-pending window
        /// (EPERM/EACCES/EBUSY-alikes) are both transient contention — back off, never fail hard.
        /// B1: when the create itself succeeded but the write failed, the empty artifact this
        /// process provably created is best-effort deleted before returning — race-safe, because
        /// a milliseconds-old artifact is never a takeover candidate and exclusive-create excludes
        /// peers, so the path can only hold our own file at cleanup time.
        /// </summary>
        private bool TryExclusiveCreateWithContent(string path, byte[] content)
        {
            var created = false;
            try
            {
                // FileMode.CreateNew is the mutual-exclusion primitive — atomic on every OS and
                // every filesystem we support, including NFS/SMB (O6). FileShare.Read lets a
                // peer's staleness read proceed during the microseconds the handle is open.
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    created = true;
                    WriteFaultInjection?.Invoke(); // test seam (B1); no-op in production
                    stream.Write(content, 0, content.Length);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (created)
                    TryDeleteFileBestEffort(path); // B1: no poisoned zero-byte artifact survives
                return false;
            }
            return true;
        }

        // ── Stale takeover: compare-and-delete, serialized through the intent file (04 §2) ──────

        /// <summary>
        /// Stale-takeover attempt: returns true when the stale lock was REMOVED (the caller then
        /// races exclusive-create immediately); false when there is nothing stale, we lost a race,
        /// or a live claimant's intent told us to back off. Mirrors the TS twin's
        /// <c>tryStaleTakeover</c> with the owner-adopted narrowings: intent read-back
        /// verification after create, and intent ownership re-verification immediately before the
        /// lock delete.
        /// </summary>
        private bool TryRemoveStaleLock()
        {
            if (!File.Exists(_lockPath))
                return false; // gone — the caller's next exclusive-create attempt races for it

            // stat №1: last-WRITE time (never last-access: atime is unreliable and touched by the
            // peers' own staleness reads) + size, from one fresh FileInfo refresh.
            DateTime mtime1;
            long size1;
            try
            {
                var info = new FileInfo(_lockPath);
                mtime1 = info.LastWriteTimeUtc;
                size1 = info.Length;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }

            var content1 = TryReadAllBytes(_lockPath);
            if (content1 == null)
                return false; // vanished or unreadable right now — retry later

            var documentClass = ClassifyLockDocument(content1);
            if ((DateTime.UtcNow - mtime1).TotalMilliseconds < StalenessThresholdMs(documentClass))
                return false; // a live (or not-provably-dead) holder — keep waiting

            // Serialize claimants: only the winner of the intent file's exclusive-create may
            // remove the stale artifact. Without this arbiter, two lockstep claimants both
            // validate the SAME unchanged stale file and the slower delete lands on a freshly
            // re-created LIVE lock (measured in the TS twin: 3/25 lockstep rounds double-acquired).
            var intentContent = TryAcquireTakeoverIntent();
            if (intentContent == null)
                return false; // a live claimant is mid-takeover (or we lost the intent race) — back off

            try
            {
                AfterIntentAcquiredInjection?.Invoke(); // test seam; no-op in production

                // Re-validate under the intent: remove only the exact artifact we judged stale
                // (mtime + size + byte-identical content — a live replacement always differs, its
                // startedAt/nonce are newer than the dead holder's).
                DateTime mtime2;
                long size2;
                try
                {
                    var info2 = new FileInfo(_lockPath);
                    mtime2 = info2.LastWriteTimeUtc;
                    size2 = info2.Length;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return false; // released or removed while we acquired the intent
                }

                var content2 = TryReadAllBytes(_lockPath);
                if (content2 == null)
                    return false;
                if (mtime2 != mtime1 || size2 != size1 || !BytesEqual(content1, content2))
                    return false; // replaced by a live lock while we acquired the intent — leave it

                // Re-verify intent ownership immediately before the delete: if a crashed-intent
                // recovery replaced our intent in the meantime, the arbiter is no longer ours and
                // we must not act under it.
                var intentNow = TryReadAllBytes(_intentPath);
                if (intentNow == null || !BytesEqual(intentNow, intentContent))
                    return false;

                try
                {
                    File.Delete(_lockPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return false; // somebody else's delete won — retry
                }

                if (documentClass == LockDocumentClass.Unparseable)
                {
                    // One diagnostic per takeover of a corrupt artifact (fix-round amendment,
                    // AFTER the successful removal — mirroring the TS twin): its writer never
                    // entered the critical section, but its existence usually means a crashed or
                    // interrupted acquisition worth surfacing. Lock documents carry no token
                    // material. Intent recovery is intentionally silent (mirrored asymmetry).
                    Warn("credential lock takeover: removed an unparseable (zero-byte or corrupted) lock artifact at "
                        + _lockPath + "; treated as stale at LOCK_STALE_MS because its writer can never have "
                        + "entered the critical section");
                }
                return true;
            }
            finally
            {
                ReleaseTakeoverIntent(intentContent);
            }
        }

        /// <summary>
        /// Win the takeover-intent file by exclusive-create (read back to verify ownership),
        /// recovering a CRASHED claimant's intent by the same staleness + compare-and-delete
        /// rules. Returns the intent content bytes on success, or null when a live claimant holds
        /// it (back off). The crashed-intent recovery is deliberately unserialized (there is no
        /// third lock): it is reachable only when a claimant died between intent-create and
        /// intent-release, and its residual is another claimant pair racing the MAIN takeover —
        /// two small probabilities multiplied, accepted by design in both languages.
        /// </summary>
        private byte[]? TryAcquireTakeoverIntent()
        {
            var content = BuildOwnDocument();

            for (var createAttempt = 0; createAttempt < 2; createAttempt++)
            {
                if (TryExclusiveCreateWithContent(_intentPath, content))
                {
                    AfterIntentCreateInjection?.Invoke(); // test seam; no-op in production

                    // Read back and verify the intent is OURS (owner-adopted narrowing): a racing
                    // crashed-intent recovery could have deleted and replaced it already.
                    var readBack = TryReadAllBytes(_intentPath);
                    if (readBack == null || !BytesEqual(readBack, content))
                        return null; // displaced — the removal authority is not ours; never delete theirs
                    return content;
                }

                // Create failed. If no intent exists (delete-pending window / vanished), retry the
                // exclusive-create once; if one exists, recover it only when its claimant crashed.
                if (!File.Exists(_intentPath))
                    continue;

                DateTime imtime1;
                try
                {
                    imtime1 = File.GetLastWriteTimeUtc(_intentPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return null;
                }

                var icontent1 = TryReadAllBytes(_intentPath);
                if (icontent1 == null)
                    continue; // vanished — retry the exclusive-create once

                // Same classification rules as the lock itself: an unparseable/zero-byte intent's
                // writer never completed intent acquisition, so it gets the short threshold too
                // (a killed creator must not wedge takeover for 24 h). Recovery is silent.
                var ithresholdMs = StalenessThresholdMs(ClassifyLockDocument(icontent1));
                if ((DateTime.UtcNow - imtime1).TotalMilliseconds < ithresholdMs)
                    return null; // a live claimant is mid-takeover — back off

                // Compare-and-delete the crashed intent, then retry the exclusive-create once.
                DateTime imtime2;
                try
                {
                    imtime2 = File.GetLastWriteTimeUtc(_intentPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return null;
                }
                var icontent2 = TryReadAllBytes(_intentPath);
                if (icontent2 == null)
                    continue;
                if (imtime2 != imtime1 || !BytesEqual(icontent1, icontent2))
                    return null;
                try
                {
                    File.Delete(_intentPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>Release the takeover-intent file — only when it still carries OUR content.</summary>
        private void ReleaseTakeoverIntent(byte[] ownContent)
        {
            var current = TryReadAllBytes(_intentPath);
            if (current == null)
                return; // already gone
            if (!BytesEqual(current, ownContent))
                return; // a crashed-intent recovery replaced it — it is someone else's now

            TryDeleteFileBestEffort(_intentPath);
        }

        // ── Lock document {pid, startedAt, hostId, nonce} (04 §2 + A2 nonce amendment) ───────────

        private sealed class LockDocument
        {
            public long Pid { get; set; }
            public string? StartedAt { get; set; }
            public string? HostId { get; set; }
            public string? Nonce { get; set; }
        }

        private byte[] BuildOwnDocument()
        {
            var document = new LockDocument
            {
                Pid = CurrentPid,
                StartedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                HostId = _hostId,
                Nonce = NewNonce(), // fresh 128-bit hex per acquisition attempt: makes every document unique
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

        private static string NewNonce()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var builder = new StringBuilder(32);
            foreach (var b in bytes)
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        /// <summary>
        /// Staleness class of a lock/intent document (mirror of the TS twin's
        /// <c>classifyLockDocument</c>): <c>Local</c>/<c>Unparseable</c> take the short bar,
        /// <c>Foreign</c> the 24 h bar — see <see cref="StalenessThresholdMs"/>.
        /// </summary>
        internal enum LockDocumentClass
        {
            Local,
            Foreign,
            Unparseable,
        }

        /// <summary>
        /// Classify a lock/intent document (one shared classifier for BOTH files, mirroring the
        /// TS twin's <c>classifyLockDocument</c>): invalid JSON, zero bytes, or any non-object
        /// JSON → <see cref="LockDocumentClass.Unparseable"/> (a torn or alien artifact — its
        /// writer never completed a protocol acquire); JSON object with a missing, empty, or
        /// non-string <c>hostId</c> → <see cref="LockDocumentClass.Foreign"/> (a completed write
        /// proving nothing about which machine wrote it); otherwise the ordinal case-insensitive
        /// <c>hostId</c> comparison decides Local vs Foreign (the pinned cross-language semantic —
        /// TS folds via <c>toLowerCase()</c>, diverging only on non-ASCII hostnames, in the
        /// conservative direction). Unknown extra fields are tolerated.
        /// </summary>
        internal LockDocumentClass ClassifyLockDocument(byte[] content)
        {
            if (content.Length == 0)
                return LockDocumentClass.Unparseable;

            try
            {
                using (var document = JsonDocument.Parse(content))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return LockDocumentClass.Unparseable;
                    if (!root.TryGetProperty("hostId", out var host) || host.ValueKind != JsonValueKind.String)
                        return LockDocumentClass.Foreign;
                    var hostId = host.GetString();
                    if (string.IsNullOrEmpty(hostId))
                        return LockDocumentClass.Foreign;
                    return string.Equals(hostId, _hostId, StringComparison.OrdinalIgnoreCase)
                        ? LockDocumentClass.Local
                        : LockDocumentClass.Foreign;
                }
            }
            catch (JsonException)
            {
                // Torn/partial write: the writer was killed between create and write (or the
                // artifact is a cleaned-up write failure). It provably never entered the critical
                // section — short bar (B2, owner-adopted).
                return LockDocumentClass.Unparseable;
            }
        }

        /// <summary>The staleness bar for a document class (B2 amendment: unparseable = short bar).</summary>
        private long StalenessThresholdMs(LockDocumentClass documentClass)
            => documentClass == LockDocumentClass.Foreign ? _foreignStaleMs : _staleMs;

        private static string ResolveLocalHostId()
        {
            try
            {
                // Same gethostname source as Node's os.hostname(), so the TS twin's locks are
                // recognised as local. Environment.MachineName (NetBIOS, upper-cased, 15-char
                // truncated on Windows) would NOT match it and is only the exception fallback —
                // over-conservative direction only (a mismatch means the foreign bar, never a
                // wrongful takeover).
                return System.Net.Dns.GetHostName();
            }
            catch (Exception)
            {
                return Environment.MachineName;
            }
        }

        private void Warn(string message)
        {
            var sink = _diagnostics;
            if (sink != null)
            {
                try
                {
                    sink(message);
                }
                catch
                {
                    // A diagnostics sink must never break the protocol.
                }
                return;
            }
            Trace.TraceWarning(message);
        }

        private static int NextBackoffMs(Random random, int attempt)
        {
            // Mirror the TS twin: base 25 ms doubled per attempt (exponent capped), capped at
            // 500 ms, then jittered ×[0.5, 1.5).
            var uncapped = (long)BackoffBaseMs << Math.Min(attempt, 10);
            var capped = (int)Math.Min(uncapped, BackoffCapMs);
            return Math.Max(1, (int)Math.Round(capped * (0.5 + random.NextDouble())));
        }

        // ── Shared low-level helpers (also used by the handle) ───────────────────────────────────

        /// <summary>
        /// Read a protocol file without ever blocking a peer's rename/delete
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

        internal static void TryDeleteFileBestEffort(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort: an undeletable artifact ages into stale takeover (04 §2).
            }
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
        /// wrote — i.e. the lock has not been (wrongly) taken over. The per-acquisition nonce
        /// makes the comparison unambiguous even across attempts of the same process. Used by the
        /// acquire path's verify-own-content step (04 §2) and by holders that want to assert their
        /// own integrity after a long critical section (recommended for b3: check immediately
        /// before the store write).
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

            MachineCredentialLock.TryDeleteFileBestEffort(_lockPath);
        }
    }
}
