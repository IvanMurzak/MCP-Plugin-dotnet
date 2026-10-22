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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using com.IvanMurzak.McpPlugin.AgentConfig;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// The on-disk half of <see cref="PluginCredentialProvider"/>'s dead-family memo (04 §3.5
    /// "never loop"). The in-memory memo dies with the process — and with every Unity domain reload —
    /// so without this sidecar each reload re-presents a refresh token the server already declared
    /// dead (one guaranteed HTTP 400 per reload). The sidecar sits next to the machine store
    /// (<see cref="MachineCredentialStore.BaseDirectory"/>) and holds ONLY a SHA-256 hash of the dead
    /// refresh token, the surfaced reason, and a timestamp — never the token itself.
    /// <para>Every operation is best-effort: a failed read is "no memo" (at worst one extra network
    /// attempt), and a failed write or delete is logged at debug level and swallowed — this class
    /// never throws into the refresh path.</para>
    /// </summary>
    internal sealed class PersistedDeadFamilyMemo
    {
        /// <summary>Sidecar file name inside the store directory.</summary>
        internal const string FileName = "plugin-dead-family.json";

        readonly string _path;
        readonly ILogger? _logger;

        internal PersistedDeadFamilyMemo(string baseDirectory, ILogger? logger)
        {
            _path = Path.Combine(baseDirectory, FileName);
            _logger = logger;
        }

        /// <summary>
        /// True when the sidecar declares <paramref name="refreshToken"/> dead; <paramref name="reason"/>
        /// then carries the reason recorded with the verdict. False when there is no (readable) sidecar
        /// or it names a different token.
        /// </summary>
        internal bool Matches(string refreshToken, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (!File.Exists(_path))
                    return false;
                using var document = JsonDocument.Parse(File.ReadAllText(_path, Encoding.UTF8));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("refreshTokenSha256", out var hash)
                    || hash.ValueKind != JsonValueKind.String
                    || !string.Equals(hash.GetString(), Hash(refreshToken), StringComparison.Ordinal))
                    return false;
                reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString() ?? string.Empty
                    : string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Reading the dead-family memo failed: {message}", ex.Message);
                return false;
            }
        }

        /// <summary>Record <paramref name="refreshToken"/> (hashed) as dead, via the store's atomic write (0600 on POSIX).</summary>
        internal void Write(string refreshToken, string reason, DateTimeOffset at)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(_path)))
                    return; // no store directory ⇒ no credential to memoize against

                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", 1);
                    writer.WriteString("refreshTokenSha256", Hash(refreshToken));
                    writer.WriteString("reason", reason);
                    writer.WriteString("declaredDeadAt", at.ToUniversalTime().ToString("o"));
                    writer.WriteEndObject();
                }
                MachineCredentialStore.WriteFileAtomic(_path, buffer.ToArray());
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Persisting the dead-family memo failed: {message}", ex.Message);
            }
        }

        /// <summary>Remove the sidecar (re-arm). No-op when absent.</summary>
        internal void Clear()
        {
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Deleting the dead-family memo failed: {message}", ex.Message);
            }
        }

        internal static string Hash(string refreshToken)
        {
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
            var hex = new StringBuilder(digest.Length * 2);
            foreach (var b in digest)
                hex.Append(b.ToString("x2"));
            return hex.ToString();
        }
    }
}
