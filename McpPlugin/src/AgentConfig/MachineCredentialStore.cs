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

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The shared machine credential store at <c>~/.ai-game-dev/</c> — a single per-machine home for
    /// the ai-game.dev account credential (<c>credentials.json</c>) and the co-located public JWKS
    /// cache (<c>jwks-cache.json</c>). Engines, CLIs, and the local server all read this store, so
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

        /// <summary>True when a credential file exists in the store.</summary>
        public bool Exists => File.Exists(CredentialsPath);

        /// <summary>
        /// Read and decrypt the stored credentials, or <c>null</c> when none are present.
        /// </summary>
        public MachineCredentials? Read()
        {
            if (!File.Exists(CredentialsPath))
                return null;

            var raw = File.ReadAllBytes(CredentialsPath);
            if (raw.Length == 0)
                return null;

            var plaintext = IsWindows ? Unprotect(raw) : raw;
            var json = Encoding.UTF8.GetString(plaintext);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<MachineCredentials>(json, SerializerOptions);
        }

        /// <summary>
        /// Encrypt (Windows) / restrict (POSIX) and write <paramref name="credentials"/> to the store,
        /// creating the store directory with owner-only permissions if needed.
        /// </summary>
        public void Write(MachineCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            EnsureBaseDirectory();

            var json = JsonSerializer.Serialize(credentials, SerializerOptions);
            var plaintext = Encoding.UTF8.GetBytes(json);
            var bytes = IsWindows ? Protect(plaintext) : plaintext;

            File.WriteAllBytes(CredentialsPath, bytes);
            SetPosixPermissions(CredentialsPath, PosixFilePermissions);
        }

        /// <summary>
        /// Replace the token material (access + refresh + expiry) while preserving the stored identity
        /// fields (<see cref="MachineCredentials.ServerTarget"/> / <see cref="MachineCredentials.Subject"/>),
        /// then persist. Returns the written credentials.
        /// </summary>
        public MachineCredentials Rotate(string accessToken, string refreshToken, DateTimeOffset? expiresAt = null)
        {
            var current = Read() ?? new MachineCredentials();
            current.AccessToken = accessToken;
            current.RefreshToken = refreshToken;
            current.ExpiresAt = expiresAt;
            Write(current);
            return current;
        }

        /// <summary>Delete the stored credentials (sign-out). No-op when none exist.</summary>
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

        // ── Windows DPAPI (CryptProtectData / CryptUnprotectData) via P/Invoke ───────────────────
        // Self-contained (no NuGet dependency added to this Unity-consumed library). Only invoked on
        // Windows; the DllImports compile on every TFM but are never called on POSIX.

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
