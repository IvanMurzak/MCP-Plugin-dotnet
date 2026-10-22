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
using System.Globalization;
using System.IO;
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
    /// <para><b>Shared file (cross-language contract):</b> AI-Game-Dev-CLI-Core's
    /// <c>PersistedDeadFamilyMemo</c> (<c>dead-family-memo.ts</c>) reads and writes the SAME file, so
    /// a verdict reached by any component stops every other one. Both write byte-identical
    /// documents — <c>JSON.stringify({ version: 1, refreshTokenSha256, reason, declaredDeadAt }, null, 2)</c>:
    /// 2-space indent, LF, no trailing newline, UTF-8 without BOM, the token hash as lowercase hex
    /// SHA-256 of its UTF-8 bytes, the timestamp as <c>Date.toISOString()</c> (UTC, milliseconds,
    /// <c>Z</c>). Both read it alike: a record matches on the exact hash string, <c>version</c> is
    /// not checked, and a record whose reason names a client-id mismatch is ignored: such a token is live for its real mint client (04 §3 rule 7). Pinned by
    /// <c>DeadFamilyMemo.GoldenVectors.json</c>.</para>
    /// <para>Every operation is best-effort: a failed read is "no memo" (at worst one extra network
    /// attempt), and a failed write or delete is logged at debug level and swallowed — this class
    /// never throws into the refresh path.</para>
    /// </summary>
    internal sealed class PersistedDeadFamilyMemo
    {
        /// <summary>Sidecar file name inside the store directory.</summary>
        internal const string FileName = "plugin-dead-family.json";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        readonly string _path;
        readonly ILogger? _logger;

        internal PersistedDeadFamilyMemo(string baseDirectory, ILogger? logger)
        {
            _path = Path.Combine(baseDirectory, FileName);
            _logger = logger;
        }

        /// <summary>
        /// True when the sidecar declares <paramref name="refreshToken"/> dead; <paramref name="reason"/>
        /// then carries the reason recorded with the verdict. False when there is no (readable) sidecar,
        /// it names a different token, or its reason is a client-id mismatch (not a verdict).
        /// </summary>
        internal bool Matches(string refreshToken, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (!File.Exists(_path))
                    return false;
                return TryMatch(File.ReadAllText(_path, Encoding.UTF8), refreshToken, out reason);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("Reading the dead-family memo failed: {message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The parse-and-match half of <see cref="Matches"/> over the sidecar's text (pure — pinned
        /// by the golden vectors). Throws on malformed JSON; <see cref="Matches"/> turns that into "no memo".
        /// </summary>
        internal static bool TryMatch(string documentText, string refreshToken, out string reason)
        {
            reason = string.Empty;
            using var document = JsonDocument.Parse(documentText);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("refreshTokenSha256", out var hash)
                || hash.ValueKind != JsonValueKind.String
                || !string.Equals(hash.GetString(), Hash(refreshToken), StringComparison.Ordinal))
                return false;
            var recorded = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? string.Empty
                : string.Empty;
            // A recorded client-id mismatch is not a verdict (04 §3 rule 7) — e.g. one written by
            // McpPlugin 8.11.x before the mismatch had its own failure kind.
            if (RefreshClientIdFallback.IsClientIdMismatchText(recorded))
                return false;
            reason = recorded;
            return true;
        }

        /// <summary>
        /// Record <paramref name="refreshToken"/> (hashed) as dead, via the store's atomic write
        /// (0600 on POSIX). The provider never calls this for a client-id mismatch (04 §3 rule 7).
        /// </summary>
        internal void Write(string refreshToken, string reason, DateTimeOffset at)
        {
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(_path)))
                    return; // no store directory ⇒ no credential to memoize against

                MachineCredentialStore.WriteFileAtomic(_path, Utf8NoBom.GetBytes(Serialize(refreshToken, reason, at)));
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

        /// <summary>Lowercase hex SHA-256 of the UTF-8 refresh token (cli-core <c>hashRefreshToken</c>).</summary>
        internal static string Hash(string refreshToken) => HexEncoding.Sha256Hex(refreshToken);

        /// <summary>
        /// The sidecar document exactly as cli-core writes it:
        /// <c>JSON.stringify({ version: 1, refreshTokenSha256, reason, declaredDeadAt }, null, 2)</c>.
        /// </summary>
        internal static string Serialize(string refreshToken, string reason, DateTimeOffset at)
            => "{\n"
                + "  \"version\": 1,\n"
                + "  \"refreshTokenSha256\": " + JsonString(Hash(refreshToken)) + ",\n"
                + "  \"reason\": " + JsonString(reason) + ",\n"
                + "  \"declaredDeadAt\": " + JsonString(ToIsoString(at)) + "\n"
                + "}";

        /// <summary>JavaScript <c>Date.prototype.toISOString()</c>: UTC, millisecond precision (truncated), <c>Z</c>.</summary>
        internal static string ToIsoString(DateTimeOffset at)
            => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>
        /// A JSON string literal exactly as <c>JSON.stringify</c> writes it: <c>"</c> and <c>\</c>
        /// escaped, the short escapes for <c>\b \f \n \r \t</c>, every other control character and
        /// every lone surrogate as lowercase <c>\uxxxx</c>, everything else (non-ASCII, <c>&lt;</c>,
        /// <c>'</c>, <c>/</c>, U+2028) verbatim — unlike System.Text.Json's default encoder.
        /// </summary>
        static string JsonString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); continue;
                    case '\\': sb.Append("\\\\"); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                var loneSurrogate = char.IsHighSurrogate(c)
                    ? !(i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    : char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(value[i - 1]));
                if (c < ' ' || loneSurrogate)
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
