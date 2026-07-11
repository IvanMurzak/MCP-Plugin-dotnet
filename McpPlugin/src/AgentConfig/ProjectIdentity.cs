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
using System.Security.Cryptography;
using System.Text;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The single canonical derivation of a project's <b>routing pin</b> and its
    /// <b>deterministic local port</b> from the project root path.
    ///
    /// <para>
    /// This is the reference implementation of the algorithm shipped in the Unity plugin as
    /// <c>UnityMcpPlugin.GeneratePortFromDirectory()</c> and ported to the unity/unreal TS CLIs.
    /// Single-sourcing it here (used by the Unity/Godot plugins AND the Unreal .NET sidecar) means
    /// every runtime derives identical values with no shared state and no probing.
    /// </para>
    ///
    /// <para><b>Algorithm (kept verbatim from the shipped Unity code):</b></para>
    /// <list type="number">
    ///   <item>Take the project root string, trim trailing directory separators
    ///         (so <c>/a/b</c> and <c>/a/b/</c> are the same project), then lowercase it with
    ///         <see cref="string.ToLowerInvariant"/>.</item>
    ///   <item>UTF-8 encode and SHA-256 hash the result.</item>
    ///   <item><b>Pin</b> = the first 4 bytes of the hash as 8 lowercase hex chars.</item>
    ///   <item><b>Port</b> = <c>20000 + (littleEndianUInt32(firstFourBytes) % 10000)</c>,
    ///         i.e. range 20000-29999.</item>
    /// </list>
    ///
    /// <para><b>Port compatibility guarantee:</b> because the algorithm is byte-for-byte the shipped
    /// Unity <c>GeneratePortFromDirectory</c>, feeding it the same directory string an existing Unity
    /// project reports as its <c>Environment.CurrentDirectory</c> yields the same port an existing
    /// user already has. The only intentional difference is <b>which string is hashed</b>: this API
    /// hashes the explicit normalized project root the caller supplies (the plugin's reported project
    /// path), NOT <c>Environment.CurrentDirectory</c> — so the pin/port are stable regardless of the
    /// process's working directory.</para>
    ///
    /// <para><b>Little-endian is explicit</b> (byte shifts, not <see cref="BitConverter"/>) so the
    /// value is identical on any CPU endianness and matches the TS port's <c>readInt32LE</c>.</para>
    ///
    /// <para>Cross-language parity (C# vs TS) is gated by the committed golden-vector file
    /// (<c>ProjectIdentity.GoldenVectors.json</c>), which explicitly pins the C#
    /// <c>ToLowerInvariant</c> vs JS <c>toLowerCase()</c> Unicode divergence, separator handling,
    /// and trailing-slash handling.</para>
    /// </summary>
    public readonly struct ProjectIdentity
    {
        /// <summary>Inclusive lower bound of the deterministic local-port range.</summary>
        public const int MinPort = 20000;

        /// <summary>Inclusive upper bound of the deterministic local-port range.</summary>
        public const int MaxPort = 29999;

        /// <summary>Number of ports in the deterministic range (10000).</summary>
        public const int PortRange = MaxPort - MinPort + 1;

        /// <summary>Number of hex characters in the routing pin (first 4 bytes of the hash).</summary>
        public const int PinLength = 8;

        /// <summary>The routing pin: first 8 lowercase hex chars of the SHA-256 of the normalized project root.</summary>
        public string Pin { get; }

        /// <summary>
        /// The resolved local port. Equal to <see cref="DerivePort"/> unless an explicit user port
        /// override was supplied (from the project marker), in which case the override wins.
        /// </summary>
        public int Port { get; }

        /// <summary>True when <see cref="Port"/> came from an explicit user override rather than the hash.</summary>
        public bool PortIsOverridden { get; }

        private ProjectIdentity(string pin, int port, bool portIsOverridden)
        {
            Pin = pin;
            Port = port;
            PortIsOverridden = portIsOverridden;
        }

        /// <summary>
        /// Derive the identity for <paramref name="projectRoot"/>. When <paramref name="portOverride"/>
        /// is non-null (the user's explicit override from the project marker) it always wins for
        /// <see cref="Port"/>; the <see cref="Pin"/> is always hash-derived.
        /// </summary>
        /// <param name="projectRoot">The project root path (normalized project root the plugin reports).</param>
        /// <param name="portOverride">Optional explicit user port override; wins when set.</param>
        public static ProjectIdentity Derive(string projectRoot, int? portOverride = null)
        {
            if (projectRoot == null)
                throw new ArgumentNullException(nameof(projectRoot));

            var hash = HashOf(projectRoot);
            var pin = ToHex(hash, PinLength / 2);
            var derivedPort = PortFromHash(hash);

            if (portOverride.HasValue)
                return new ProjectIdentity(pin, portOverride.Value, portIsOverridden: true);

            return new ProjectIdentity(pin, derivedPort, portIsOverridden: false);
        }

        /// <summary>
        /// Convenience overload that reads the port override from a project marker (may be null).
        /// </summary>
        public static ProjectIdentity Derive(string projectRoot, ProjectMarker? marker)
            => Derive(projectRoot, marker?.PortOverride);

        /// <summary>The routing pin only (first 8 lowercase hex chars of the hash). Never affected by overrides.</summary>
        public static string DerivePin(string projectRoot)
        {
            if (projectRoot == null)
                throw new ArgumentNullException(nameof(projectRoot));
            return ToHex(HashOf(projectRoot), PinLength / 2);
        }

        /// <summary>
        /// The pure hash-derived port (ignores any override). This is the byte-for-byte equivalent of
        /// the shipped Unity <c>GeneratePortFromDirectory</c> when given the same directory string.
        /// </summary>
        public static int DerivePort(string projectRoot)
        {
            if (projectRoot == null)
                throw new ArgumentNullException(nameof(projectRoot));
            return PortFromHash(HashOf(projectRoot));
        }

        /// <summary>
        /// The exact string that is UTF-8/SHA-256 hashed: the project root with trailing directory
        /// separators trimmed, then lowercased with <see cref="string.ToLowerInvariant"/>. Exposed so
        /// the golden-vector tooling and cross-language ports can reproduce the pre-hash string.
        /// </summary>
        public static string Normalize(string projectRoot)
        {
            if (projectRoot == null)
                throw new ArgumentNullException(nameof(projectRoot));
            return TrimTrailingSeparators(projectRoot).ToLowerInvariant();
        }

        internal static byte[] HashOf(string projectRoot)
        {
            var normalized = Normalize(projectRoot);
            var bytes = Encoding.UTF8.GetBytes(normalized);
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(bytes);
            }
        }

        internal static int PortFromHash(byte[] hash)
        {
            // First 4 bytes as an explicit little-endian uint32 (matches Unity's BitConverter.ToInt32
            // on little-endian platforms and the TS port's Buffer.readInt32LE, independent of CPU).
            var value = (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));
            return MinPort + (int)(value % PortRange);
        }

        private static string TrimTrailingSeparators(string path)
        {
            var end = path.Length;
            while (end > 1 && (path[end - 1] == '/' || path[end - 1] == '\\'))
                end--;
            return end == path.Length ? path : path.Substring(0, end);
        }

        private static string ToHex(byte[] bytes, int count)
        {
            var sb = new StringBuilder(count * 2);
            for (var i = 0; i < count; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
