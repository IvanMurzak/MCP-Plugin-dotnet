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
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// One cached Cloud project key (project-keys contract §6). <see cref="Key"/> is the raw
    /// <c>agd_pk_…</c> secret; the rest identifies what it was minted for, so a reader can tell whether the
    /// cached key still belongs to the currently signed-in account (<see cref="Sub"/>) and issuer.
    /// </summary>
    public sealed class ProjectKeyEntry
    {
        public string Key { get; set; } = string.Empty;
        public string? KeyId { get; set; }
        public string Pin { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string? Sub { get; set; }
        public string? Engine { get; set; }
        /// <summary>ISO-8601 creation instant as returned by the server (kept verbatim).</summary>
        public string? CreatedAt { get; set; }
    }

    /// <summary>
    /// The local project-key cache at <c>~/.ai-game-dev/project-keys.json</c> (project-keys contract §6).
    ///
    /// <para>A SEPARATE file from <c>credentials.json</c> so an older writer of that file can never drop the
    /// keys. Same protection as <see cref="MachineCredentialStore"/>, byte-for-byte: on Windows the file is the
    /// raw DPAPI (<c>CryptProtectData</c>, CurrentUser, null entropy, UI-forbidden) blob of the UTF-8 JSON —
    /// the identical envelope, produced by the identical P/Invoke, so the C# and TS (cli-core) implementations
    /// read each other's files; on POSIX it is plain UTF-8 JSON with mode 0600 in a 0700 directory. Writes are
    /// atomic (temp sibling + rename, fsync) and read-modify-write: unknown top-level fields, unknown entries,
    /// and unknown fields inside an entry that is overwritten all survive.</para>
    ///
    /// <para>Plain-JSON shape (before protection), pinned by <c>project-keys.golden.json</c>:
    /// <c>{"version":1,"keys":{"&lt;issuerOrigin&gt;#&lt;pin&gt;":{"key","keyId","pin","issuer","sub","engine","createdAt"}}}</c>.</para>
    /// </summary>
    public sealed class ProjectKeyStore
    {
        public const string FileName = "project-keys.json";
        public const int CurrentVersion = 1;

        private static readonly Regex PinPattern = new Regex("^[0-9a-f]{" + ProjectIdentity.PinLength + "}$", RegexOptions.CultureInvariant);
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _baseDirectory;
        private readonly bool _plaintextForTesting;

        /// <param name="baseDirectory">Override of <c>~/.ai-game-dev</c> (tests / portable installs).</param>
        public ProjectKeyStore(string? baseDirectory = null)
            : this(baseDirectory, plaintextForTesting: false)
        {
        }

        internal ProjectKeyStore(string? baseDirectory, bool plaintextForTesting)
        {
            _baseDirectory = baseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                MachineCredentialStore.DirectoryName);
            _plaintextForTesting = plaintextForTesting;
        }

        public string BaseDirectory => _baseDirectory;
        public string FilePath => Path.Combine(_baseDirectory, FileName);

        // ── Naming (golden-vector pinned) ─────────────────────────────────────────────────────────

        /// <summary>
        /// Normalises an issuer URL to its origin: lower-case scheme and host, default port dropped, any
        /// path / query / fragment / trailing slash removed (the WHATWG <c>URL.origin</c> the TS side uses).
        /// <c>HTTPS://AI-Game.dev:443/api/</c> → <c>https://ai-game.dev</c>.
        /// </summary>
        public static string NormalizeIssuerOrigin(string issuer)
        {
            if (string.IsNullOrWhiteSpace(issuer)
                || !Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Issuer must be an absolute http(s) URL: '" + issuer + "'.", nameof(issuer));

            return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant();
        }

        /// <summary>Normalises and validates a v2 project pin (8 hex chars; case-insensitive input, lower-case output).</summary>
        public static string NormalizePin(string pin)
        {
            var normalized = (pin ?? string.Empty).Trim().ToLowerInvariant();
            if (!PinPattern.IsMatch(normalized))
                throw new ArgumentException("Project pin must be 8 hex characters: '" + pin + "'.", nameof(pin));
            return normalized;
        }

        /// <summary>The cache entry name: <c>&lt;issuerOrigin&gt;#&lt;pin&gt;</c>.</summary>
        public static string EntryName(string issuer, string pin) => NormalizeIssuerOrigin(issuer) + "#" + NormalizePin(pin);

        // ── Read / write ──────────────────────────────────────────────────────────────────────────

        /// <summary>The cached entry for <paramref name="issuer"/> + <paramref name="pin"/>, or <c>null</c>
        /// when absent, malformed, or the file is unreadable (see <see cref="IsUnreadable"/> to tell those apart).</summary>
        public ProjectKeyEntry? Get(string issuer, string pin)
        {
            var root = ReadDocument(out _);
            if (root?["keys"] is not JsonObject keys)
                return null;
            if (keys[EntryName(issuer, pin)] is not JsonObject node)
                return null;

            var key = GetString(node, "key");
            if (string.IsNullOrEmpty(key))
                return null;

            return new ProjectKeyEntry
            {
                Key = key!,
                KeyId = GetString(node, "keyId"),
                Pin = GetString(node, "pin") ?? NormalizePin(pin),
                Issuer = GetString(node, "issuer") ?? NormalizeIssuerOrigin(issuer),
                Sub = GetString(node, "sub"),
                Engine = GetString(node, "engine"),
                CreatedAt = GetString(node, "createdAt"),
            };
        }

        /// <summary>
        /// True when the cache file EXISTS but cannot be read (DPAPI failure, corrupt content). Such a file is
        /// never overwritten (contract §6) — callers fall back to the URL-only config and surface the error.
        /// </summary>
        public bool IsUnreadable
        {
            get
            {
                ReadDocument(out var unreadable);
                return unreadable;
            }
        }

        /// <summary>
        /// Writes <paramref name="entry"/> under <c>&lt;entry.Issuer origin&gt;#&lt;entry.Pin&gt;</c>, replacing the
        /// known fields of any existing entry and keeping everything else in the file. The read-modify-write
        /// runs under the cross-process <c>credentials.lock</c> (contract §6).
        /// </summary>
        /// <exception cref="IOException">The lock is busy, or the existing file is unreadable (never overwritten).</exception>
        public void Put(ProjectKeyEntry entry) => PutCore(entry, keepConcurrentWrite: false, keyReadBeforeMint: null);

        /// <summary>
        /// Stores a key minted after reading <paramref name="keyReadBeforeMint"/> (null = there was no entry) — UNLESS,
        /// once the lock is held, the entry for the same issuer/pin/account now carries a DIFFERENT key: another
        /// process minted concurrently, and contract §6 says keep theirs. Returns the key that is cached afterwards.
        /// </summary>
        /// <exception cref="IOException">The lock is busy, or the existing file is unreadable (never overwritten).</exception>
        public string PutUnlessConcurrentlyReplaced(ProjectKeyEntry entry, string? keyReadBeforeMint)
            => PutCore(entry, keepConcurrentWrite: true, keyReadBeforeMint);

        private string PutCore(ProjectKeyEntry entry, bool keepConcurrentWrite, string? keyReadBeforeMint)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrEmpty(entry.Key))
                throw new ArgumentException("Project key must not be empty.", nameof(entry));

            var issuer = NormalizeIssuerOrigin(entry.Issuer);
            var pin = NormalizePin(entry.Pin);

            using var _ = AcquireLock();
            var root = ReadDocumentForWrite() ?? new JsonObject();
            if (keepConcurrentWrite
                && root["keys"] is JsonObject current
                && current[issuer + "#" + pin] is JsonObject theirs
                && GetString(theirs, "key") is string theirKey && theirKey.Length > 0
                && !string.Equals(theirKey, keyReadBeforeMint, StringComparison.Ordinal)
                && string.Equals(GetString(theirs, "sub"), entry.Sub, StringComparison.Ordinal))
                return theirKey;

            root["version"] = Math.Max(CurrentVersion, GetInt(root, "version"));
            if (root["keys"] is not JsonObject keys)
            {
                keys = new JsonObject();
                root["keys"] = keys;
            }

            var name = issuer + "#" + pin;
            if (keys[name] is not JsonObject node)
            {
                node = new JsonObject();
                keys[name] = node;
            }

            node["key"] = entry.Key;
            SetOrRemove(node, "keyId", entry.KeyId);
            node["pin"] = pin;
            node["issuer"] = issuer;
            SetOrRemove(node, "sub", entry.Sub);
            SetOrRemove(node, "engine", entry.Engine);
            SetOrRemove(node, "createdAt", entry.CreatedAt);

            WriteDocument(root);
            return entry.Key;
        }

        /// <summary>Removes the entry for <paramref name="issuer"/> + <paramref name="pin"/>. Returns whether one existed.</summary>
        public bool Remove(string issuer, string pin)
        {
            var name = EntryName(issuer, pin);
            using var _ = AcquireLock();
            var root = ReadDocumentForWrite();
            if (root?["keys"] is not JsonObject keys || !keys.Remove(name))
                return false;
            WriteDocument(root);
            return true;
        }

        // ── Test / parity seams (InternalsVisibleTo: McpPlugin.Tests) ─────────────────────────────

        internal string? ReadPlaintextJson() => File.Exists(FilePath) ? Encoding.UTF8.GetString(Decode(File.ReadAllBytes(FilePath))) : null;

        internal void WritePlaintextJson(string json) => WriteBytes(Encoding.UTF8.GetBytes(json));

        // ── Internals ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the document. <c>null</c> with <paramref name="unreadable"/> false = no file (or an empty one);
        /// <c>null</c> with <paramref name="unreadable"/> true = the file exists but cannot be decoded (DPAPI key
        /// lost, corrupt, not a JSON object) — a cache MISS for reads, but never an empty document to write over.
        /// </summary>
        private JsonObject? ReadDocument(out bool unreadable)
        {
            unreadable = false;
            if (!File.Exists(FilePath))
                return null;
            try
            {
                var raw = MachineCredentialStore.ReadAllBytesWithRetry(FilePath);
                if (raw.Length == 0)
                    return null;
                var root = JsonNode.Parse(Encoding.UTF8.GetString(Decode(raw))) as JsonObject;
                unreadable = root == null;
                return root;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return null; // deleted between the existence check and the open
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is CryptographicException || ex is JsonException)
            {
                unreadable = true;
                return null;
            }
        }

        /// <summary>The document to modify, or <c>null</c> when there is none; throws when the file is unreadable.</summary>
        private JsonObject? ReadDocumentForWrite()
        {
            var root = ReadDocument(out var unreadable);
            if (unreadable)
                throw new IOException("Refusing to overwrite an unreadable project-key cache: " + FilePath);
            return root;
        }

        /// <summary>Holds the cross-process <c>credentials.lock</c> for one read-modify-write (contract §6).</summary>
        private MachineCredentialLockHandle AcquireLock() =>
            new MachineCredentialLock(_baseDirectory).TryAcquire()
            ?? throw new IOException("The machine credential lock is busy; the project-key cache was not written.");

        private void WriteDocument(JsonObject root) =>
            WriteBytes(Encoding.UTF8.GetBytes(root.ToJsonString(WriteOptions)));

        private void WriteBytes(byte[] plaintext)
        {
            Directory.CreateDirectory(_baseDirectory);
            MachineCredentialStore.SetPosixPermissions(_baseDirectory, MachineCredentialStore.PosixDirectoryPermissions);
            var bytes = IsWindows && !_plaintextForTesting ? MachineCredentialStore.ProtectBytes(plaintext) : plaintext;
            MachineCredentialStore.WriteFileAtomic(FilePath, bytes);
            MachineCredentialStore.SetPosixPermissions(FilePath, MachineCredentialStore.PosixFilePermissions);
        }

        private byte[] Decode(byte[] raw) =>
            IsWindows && !_plaintextForTesting ? MachineCredentialStore.UnprotectBytes(raw) : raw;

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static string? GetString(JsonObject node, string name) =>
            node[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        private static int GetInt(JsonObject node, string name) =>
            node[name] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

        private static void SetOrRemove(JsonObject node, string name, string? value)
        {
            if (value == null)
                node.Remove(name);
            else
                node[name] = value;
        }
    }
}
