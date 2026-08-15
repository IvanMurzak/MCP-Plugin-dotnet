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
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The shared machine credential store at <c>~/.ai-game-dev/</c> — a single per-machine home for
    /// the ai-game.dev account credential (<c>credentials.json</c>, schema v2 — see
    /// <see cref="MachineCredentials"/>) and the co-located public JWKS cache
    /// (<c>jwks-cache.json</c>). Engines, CLIs, and the local server all read this store, so
    /// sign-in happens once per machine.
    ///
    /// <para><b>At-rest protection (security-critical):</b></para>
    /// <list type="bullet">
    ///   <item><b>POSIX</b> — <c>credentials.json</c> is written plaintext with <c>0600</c> permissions,
    ///         inside a <c>0700</c> directory.</item>
    ///   <item><b>Windows</b> — <c>credentials.json</c> content is DPAPI-encrypted with the current
    ///         user's key (<c>CryptProtectData</c>, <c>CRYPTPROTECT_UI_FORBIDDEN</c>) so the on-disk
    ///         bytes are never plaintext.</item>
    /// </list>
    ///
    /// <para><b>Write durability (design 04 §1):</b> credential writes are whole-file and atomic — a
    /// temp sibling in the same directory is written, fsynced, then renamed over the destination
    /// (<see cref="File.Replace(string, string, string)"/>, which preserves the destination's DACL on
    /// Windows, with a move fallback for the create case). The rename retries
    /// <see cref="RenameRetryAttempts"/> times with <see cref="RenameRetryDelayMs"/> ms backoff to
    /// survive transient holders (AV scanners, indexers, concurrent readers). A reader therefore
    /// never observes a truncated document.</para>
    ///
    /// <para><b>Unreadable ≠ empty (design 04 §1):</b> a store that exists but cannot be decoded
    /// (DPAPI unprotect failure after a password reset / roaming profile / service account, or a
    /// corrupted document) is a distinct <see cref="MachineCredentialStoreStatus.Unreadable"/> state
    /// surfaced by <see cref="TryRead"/> — never a crash, and never a reason to delete or overwrite
    /// the file until the user explicitly re-authorizes (an automated
    /// <see cref="Rotate(string, string, DateTimeOffset?)"/> refuses; an explicit re-auth
    /// <see cref="Write"/> replaces it).</para>
    ///
    /// <para>The JWKS cache holds only public signing keys, so it is stored plaintext (still inside the
    /// user-only directory). Credentials are NEVER written to a project file / VCS.</para>
    /// </summary>
    public sealed class MachineCredentialStore
    {
        /// <summary>Directory name under the user home that holds the store.</summary>
        public const string DirectoryName = ".ai-game-dev";

        /// <summary>File name of the secret credential document.</summary>
        public const string CredentialsFileName = "credentials.json";

        /// <summary>File name of the co-located public JWKS cache.</summary>
        public const string JwksCacheFileName = "jwks-cache.json";

        /// <summary>
        /// Attempts made to rename the fsynced temp sibling over the destination before giving up.
        /// Contract (design 04 §1): at least 4.
        /// </summary>
        public const int RenameRetryAttempts = 5;

        /// <summary>
        /// Backoff between rename attempts, in milliseconds. Contract (design 04 §1): at least 250.
        /// </summary>
        public const int RenameRetryDelayMs = 250;

        // 0600 (owner rw) and 0700 (owner rwx) as binary bit-patterns — C# has no octal literals.
        private const uint PosixFilePermissions = 0b110_000_000;      // rw- --- ---
        private const uint PosixDirectoryPermissions = 0b111_000_000; // rwx --- ---
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _baseDirectory;

        /// <summary>
        /// Create a store rooted at <paramref name="baseDirectory"/>, or at <c>~/.ai-game-dev</c> when
        /// null. The override exists for tests and for the <c>--project</c> per-project store; production
        /// callers use the default machine store.
        /// </summary>
        public MachineCredentialStore(string? baseDirectory = null)
        {
            _baseDirectory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                DirectoryName);
        }

        /// <summary>Absolute path of the store directory.</summary>
        public string BaseDirectory => _baseDirectory;

        /// <summary>Absolute path of the secret credential file.</summary>
        public string CredentialsPath => Path.Combine(_baseDirectory, CredentialsFileName);

        /// <summary>Absolute path of the public JWKS cache file.</summary>
        public string JwksCachePath => Path.Combine(_baseDirectory, JwksCacheFileName);

        /// <summary>
        /// True when a credential file exists in the store. <b>Never a signed-in signal</b> (design
        /// 04 §1): an existing file may be unreadable — use <see cref="TryRead"/> and check for
        /// <see cref="MachineCredentialStoreStatus.Ok"/> instead.
        /// </summary>
        public bool Exists => File.Exists(CredentialsPath);

        /// <summary>
        /// Read and decrypt the stored credentials, or <c>null</c> when none are present.
        /// <para>Compatibility surface: an <b>unreadable</b> store also yields <c>null</c> here (it
        /// never throws for one) — call <see cref="TryRead"/> to distinguish
        /// <see cref="MachineCredentialStoreStatus.Missing"/> from
        /// <see cref="MachineCredentialStoreStatus.Unreadable"/>.</para>
        /// </summary>
        public MachineCredentials? Read() => TryRead().Credentials;

        /// <summary>
        /// Read the store with a structured outcome (design 04 §1): <see cref="MachineCredentialStoreStatus.Missing"/>
        /// when no file exists, <see cref="MachineCredentialStoreStatus.Ok"/> with the decoded
        /// credentials (a <c>{version: 1}</c> document is adopted in-memory as
        /// <c>families.legacy</c>), or <see cref="MachineCredentialStoreStatus.Unreadable"/> when the
        /// file exists but cannot be decoded — which is <b>not</b> signed-out: never crash, never
        /// delete, never overwrite until the user explicitly re-authorizes.
        /// </summary>
        public MachineCredentialReadResult TryRead()
        {
            if (!File.Exists(CredentialsPath))
                return MachineCredentialReadResult.Missing();

            byte[] raw;
            try
            {
                raw = ReadAllBytesWithRetry(CredentialsPath);
            }
            catch (FileNotFoundException)
            {
                return MachineCredentialReadResult.Missing(); // deleted between the existence check and the open
            }
            catch (DirectoryNotFoundException)
            {
                return MachineCredentialReadResult.Missing();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return MachineCredentialReadResult.Unreadable("credential file could not be opened: " + ex.Message);
            }

            if (raw.Length == 0)
                return MachineCredentialReadResult.Unreadable("credential file is empty");

            byte[] plaintext;
            if (IsWindows)
            {
                try
                {
                    plaintext = Unprotect(raw);
                }
                catch (CryptographicException ex)
                {
                    // Password reset / roaming profile / service account — the DPAPI user key can no
                    // longer decrypt the blob. Structured state, never a crash (design 04 §1).
                    return MachineCredentialReadResult.Unreadable("DPAPI unprotect failed: " + ex.Message);
                }
            }
            else
            {
                plaintext = raw;
            }

            var json = Encoding.UTF8.GetString(plaintext);
            if (string.IsNullOrWhiteSpace(json))
                return MachineCredentialReadResult.Unreadable("credential document is blank");

            MachineCredentials? credentials;
            try
            {
                credentials = JsonSerializer.Deserialize<MachineCredentials>(json, SerializerOptions);
            }
            catch (JsonException ex)
            {
                return MachineCredentialReadResult.Unreadable("credential document is not valid JSON: " + ex.Message);
            }

            if (credentials == null)
                return MachineCredentialReadResult.Unreadable("credential document decodes to null");

            // v1 read-compat: interpret top-level token material as families.legacy (design 04 §1).
            credentials.AdoptLegacyTokens();
            return MachineCredentialReadResult.Ok(credentials);
        }

        /// <summary>
        /// Encrypt (Windows) / restrict (POSIX) and write <paramref name="credentials"/> to the store,
        /// creating the store directory with owner-only permissions if needed. The document is
        /// normalized to the v2 write contract first (v1 adoption, version upgrade, and the top-level
        /// v1 compat mirror of the plugin family — see <see cref="MachineCredentials"/>), then written
        /// atomically (temp sibling → fsync → rename-with-retry).
        /// </summary>
        public void Write(MachineCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            EnsureBaseDirectory();

            credentials.NormalizeForWrite();
            var json = JsonSerializer.Serialize(credentials, SerializerOptions);
            WriteProtected(json);
        }

        /// <summary>
        /// Replace the plugin-plane token material (access + refresh + expiry) while preserving every
        /// other stored field, then persist. In a v2 document this rotates the <c>plugin</c> family
        /// (falling back to <c>legacy</c>, creating it when the store is empty); the top-level v1
        /// mirror follows on write. Refuses (throws <see cref="InvalidOperationException"/>) when the
        /// store exists but is unreadable — an automated rotation must never destroy a store the user
        /// has not explicitly re-authorized to overwrite (design 04 §1). Returns the written credentials.
        /// </summary>
        public MachineCredentials Rotate(string accessToken, string refreshToken, DateTimeOffset? expiresAt = null)
        {
            var result = TryRead();
            if (result.Status == MachineCredentialStoreStatus.Unreadable)
                throw new InvalidOperationException(
                    "The machine credential store exists but cannot be read (" + result.Error +
                    "); sign-in is required. Refusing to overwrite it from an automated rotation.");

            var current = result.Credentials ?? new MachineCredentials();
            var families = current.Families ?? (current.Families = new MachineCredentialFamilies());
            var family = families.Plugin ?? families.Legacy;
            if (family == null)
            {
                family = new MachineCredentialFamily();
                families.Legacy = family;
            }

            family.AccessToken = accessToken;
            family.RefreshToken = refreshToken;
            family.ExpiresAt = expiresAt;

            Write(current);
            return current;
        }

        /// <summary>
        /// Delete the stored credentials — explicit sign-out only, never an error-recovery path
        /// (an unreadable store must be preserved for the user). No-op when none exist.
        /// </summary>
        public void Delete()
        {
            if (File.Exists(CredentialsPath))
                File.Delete(CredentialsPath);
        }

        /// <summary>Read the cached JWKS document (public keys), or <c>null</c> when not cached.</summary>
        public string? ReadJwksCache()
        {
            return File.Exists(JwksCachePath) ? File.ReadAllText(JwksCachePath) : null;
        }

        /// <summary>Write the JWKS document (public keys; plaintext) into the store.</summary>
        public void WriteJwksCache(string jwksJson)
        {
            if (jwksJson == null)
                throw new ArgumentNullException(nameof(jwksJson));

            EnsureBaseDirectory();
            File.WriteAllText(JwksCachePath, jwksJson);
        }

        // ── Test / diagnostic seams (InternalsVisibleTo: McpPlugin.Tests) ────────────────────────

        /// <summary>
        /// The decrypted credential document text, or <c>null</c> when missing. Test/diagnostic seam —
        /// lets raw-document assertions (mirror emission, unknown-field preservation, golden vectors)
        /// run on Windows, where the on-disk bytes are DPAPI-encrypted. Throws on an unreadable store.
        /// </summary>
        internal string? ReadPlaintextJson()
        {
            if (!File.Exists(CredentialsPath))
                return null;

            var raw = File.ReadAllBytes(CredentialsPath);
            var plaintext = IsWindows ? Unprotect(raw) : raw;
            return Encoding.UTF8.GetString(plaintext);
        }

        /// <summary>
        /// Write a raw credential document (protect + atomic write), bypassing the
        /// <see cref="MachineCredentials"/> model. Test seam for planting v1 / corrupted / newer-schema
        /// fixtures on every OS.
        /// </summary>
        internal void WritePlaintextJson(string json)
        {
            EnsureBaseDirectory();
            WriteProtected(json);
        }

        /// <summary>Unprotect raw on-disk bytes (no-op on POSIX). Test seam for concurrency probes.</summary>
        internal static byte[] UnprotectBytes(byte[] data) => IsWindows ? Unprotect(data) : data;

        /// <summary>
        /// Protect raw bytes with the store's real DPAPI codec (no-op on POSIX). Test seam
        /// (InternalsVisibleTo: McpPlugin.Tests) for the cross-implementation DPAPI parity suite
        /// (unified-machine-auth 04 §1, task x1): exercises the EXACT <c>CryptProtectData</c>
        /// P/Invoke call the store uses on Windows, so a TS-side decrypt of the result is a genuine
        /// interop proof, not a re-implementation of DPAPI.
        /// </summary>
        internal static byte[] ProtectBytes(byte[] data) => IsWindows ? Protect(data) : data;

        // ── Atomic write (design 04 §1) ──────────────────────────────────────────────────────────

        private void WriteProtected(string json)
        {
            var plaintext = Encoding.UTF8.GetBytes(json);
            var bytes = IsWindows ? Protect(plaintext) : plaintext;
            WriteFileAtomic(CredentialsPath, bytes);
            SetPosixPermissions(CredentialsPath, PosixFilePermissions);
        }

        /// <summary>
        /// Whole-file atomic write: a temp sibling <b>in the same directory</b> (same volume — a
        /// cross-volume temp would turn the rename into a copy) is created with owner-only
        /// permissions, written, fsynced, then renamed over the destination with retry. A reader can
        /// observe the old document or the new document, never a truncated one; a writer killed
        /// before the rename leaves the destination untouched (plus a stray <c>*.tmp</c> sibling the
        /// reader ignores).
        /// </summary>
        private void WriteFileAtomic(string destinationPath, byte[] bytes)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("Destination path has no directory: " + destinationPath, nameof(destinationPath));

            var tempPath = Path.Combine(directory, Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Restrict before any secret byte lands (the file is still empty here).
                    SetPosixPermissions(tempPath, PosixFilePermissions);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true); // fsync: the temp is durable before it replaces the destination
                }

                ReplaceWithRetry(tempPath, destinationPath);
            }
            catch
            {
                TryDeleteFile(tempPath);
                throw;
            }

            TryFsyncDirectory(directory); // advisory (POSIX): make the rename itself power-loss durable
        }

        /// <summary>
        /// Rename the fsynced temp sibling over the destination. Prefers
        /// <see cref="File.Replace(string, string, string)"/> (atomic; preserves the destination's
        /// DACL on Windows), falling back to a move for the create case. Retries
        /// <see cref="RenameRetryAttempts"/> times with <see cref="RenameRetryDelayMs"/> ms backoff on
        /// EBUSY / EPERM / EACCES-alike failures (AV scanners, indexers, concurrent readers holding
        /// the destination) — the retry policy is part of the store contract (design 04 §1).
        /// </summary>
        private static void ReplaceWithRetry(string tempPath, string destinationPath)
        {
            for (var attempt = 1; attempt <= RenameRetryAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(destinationPath))
                        File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
                    else
                        File.Move(tempPath, destinationPath);
                    return;
                }
                catch (Exception ex) when (attempt < RenameRetryAttempts && (ex is IOException || ex is UnauthorizedAccessException))
                {
                    // Transient holder (or the destination appeared/vanished between the existence
                    // check and the rename — FileNotFoundException is an IOException, and the next
                    // attempt re-evaluates which path to take). Back off and retry.
                    Thread.Sleep(RenameRetryDelayMs);
                }
            }

            // Unreachable: the final attempt either returned or threw (its exception filter is false).
            throw new IOException("Atomic rename retry loop exited unexpectedly for: " + destinationPath);
        }

        private static byte[] ReadAllBytesWithRetry(string path)
        {
            const int readAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (Exception ex) when (attempt < readAttempts && ex is IOException
                    && !(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException))
                {
                    // Transient sharing violation with a concurrent atomic rename — brief backoff.
                    Thread.Sleep(50);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup of the temp sibling; never mask the original failure.
            }
        }

        private void EnsureBaseDirectory()
        {
            Directory.CreateDirectory(_baseDirectory);
            SetPosixPermissions(_baseDirectory, PosixDirectoryPermissions);
        }

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static void SetPosixPermissions(string path, uint mode)
        {
#if NET8_0_OR_GREATER
            // OperatingSystem.IsWindows() is recognised by the platform-compatibility analyzer as a
            // guard for File.SetUnixFileMode (CA1416); the custom IsWindows property is not.
            if (OperatingSystem.IsWindows())
                return;
            File.SetUnixFileMode(path, (UnixFileMode)mode);
#else
            if (IsWindows)
                return;
            _ = chmod(path, mode);
#endif
        }

#if !NET8_0_OR_GREATER
        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int chmod(string path, uint mode);
#endif

        // ── POSIX directory fsync (advisory durability for the rename; design 04 §1) ─────────────

        private const int O_RDONLY = 0;

        private static void TryFsyncDirectory(string directory)
        {
            if (IsWindows)
                return;

            try
            {
                var fd = open(directory, O_RDONLY);
                if (fd < 0)
                    return;
                try
                {
                    _ = fsync(fd);
                }
                finally
                {
                    _ = close(fd);
                }
            }
            catch
            {
                // Advisory only (design 04 §1): power-loss durability parity, never a failure path.
            }
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int open(string path, int flags);

        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
        private static extern int fsync(int fd);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int close(int fd);

        // ── Windows DPAPI (CryptProtectData / CryptUnprotectData) via P/Invoke ───────────────────
        // Self-contained (no NuGet dependency added to this Unity-consumed library). Only invoked on
        // Windows; the DllImports compile on every TFM but are never called on POSIX. DPAPI use is
        // pinned by design D2: CurrentUser scope, null entropy, CRYPTPROTECT_UI_FORBIDDEN.

        private static byte[] Protect(byte[] data)
        {
            var inBlob = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                inBlob.pbData = Marshal.AllocHGlobal(data.Length);
                inBlob.cbData = data.Length;
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);

                if (!CryptProtectData(ref inBlob, "ai-game-dev credentials", IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        private static byte[] Unprotect(byte[] data)
        {
            var inBlob = new DATA_BLOB();
            var outBlob = new DATA_BLOB();
            try
            {
                inBlob.pbData = Marshal.AllocHGlobal(data.Length);
                inBlob.cbData = data.Length;
                Marshal.Copy(data, 0, inBlob.pbData, data.Length);

                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob))
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
