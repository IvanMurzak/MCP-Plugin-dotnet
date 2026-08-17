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
using com.IvanMurzak.McpPlugin.Common;
using Microsoft.AspNetCore.Http;

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// The outcome of reading the <c>AI-Game-Dev-Instance-Id</c> request header.
    /// The three states are deliberately distinct for the same reason
    /// <see cref="ProjectPinParse"/>'s are: <b>absent is not malformed</b>.
    /// </summary>
    public enum InstanceIdentityParse
    {
        /// <summary>
        /// No <c>AI-Game-Dev-Instance-Id</c> header at all. A supported, unprivileged state —
        /// EVERY currently released client is such a client. The session is tracked exactly as it is
        /// today: nothing is replaced, nothing is rejected, nothing warns.
        /// </summary>
        Absent = 0,

        /// <summary>A well-formed opaque instance id (exactly 32 hex chars) was captured.</summary>
        Valid = 1,

        /// <summary>
        /// The header is PRESENT but its value is not a well-formed opaque token. The request MUST be
        /// rejected — silently downgrading it to <see cref="Absent"/> would turn a client that believes
        /// it is participating in the replace rule into one that stacks sessions forever, with no
        /// symptom short of the 6 h idle timeout.
        /// </summary>
        Malformed = 2,
    }

    /// <summary>
    /// Reads and validates the per-installation MCP instance identity carried in the
    /// <c>AI-Game-Dev-Instance-Id</c> request header (design 07 §2.3, D10.2).
    ///
    /// <para><b>What the value is.</b> The client computes
    /// <c>HMAC-SHA256(key = installId, msg = accountSub)</c>, truncates it to 128 bits and hex-encodes
    /// it — so the wire value is exactly <see cref="ExpectedLength"/> hex characters. The server never
    /// sees <c>installId</c>, which is what makes the value unlinkable across accounts.
    /// <b>Do not "simplify" the key/message order</b>: the key is the SECRET <c>installId</c> and the
    /// message is the PUBLIC <c>accountSub</c>. (Independently re-verified twice; inverting it would
    /// require a 128-bit brute force to undo.)</para>
    ///
    /// <para><b>Why it is validated as an opaque token, and validated FIRST.</b> The value is
    /// client-supplied and therefore untrusted. Validating it to a fixed length over a closed charset
    /// before it is used anywhere means every downstream use is safe <i>by construction</i> rather than
    /// by remembering to escape: a 32-char hex string cannot carry a CR/LF log-injection payload, cannot
    /// traverse a path, and cannot terminate a quoted literal. This class is the only place the raw
    /// header value is read.</para>
    ///
    /// <para><b>It is a routing key, never an authenticator.</b> The account scope comes from the bearer
    /// credential (<see cref="ConnectionIdentity.FromPrincipal"/>); this value only ever selects among
    /// sessions <i>inside</i> that scope. A spoofed value can evict only the spoofer's own sessions.</para>
    /// </summary>
    public static class InstanceIdentityHeader
    {
        /// <summary>The header name, used byte-for-byte. No <c>X-</c> prefix (RFC 6648).</summary>
        public const string HeaderName = Consts.MCP.Server.Headers.InstanceId;

        /// <summary>
        /// Exact number of hex characters in a well-formed value: 128 bits ⇒ 16 bytes ⇒ 32 hex chars.
        /// FIXED, not a maximum — a length range would admit a truncated value that still parses, and
        /// the contract with the client is exact.
        /// </summary>
        public const int ExpectedLength = 32;

        /// <summary>Machine-readable error code returned when a present instance id cannot be parsed.</summary>
        public const string InvalidInstanceIdError = "invalid_instance_id";

        /// <summary>
        /// Reads the header from a request. Returns <see cref="InstanceIdentityParse.Absent"/> when the
        /// header is not present at all (the released-client case), <see cref="InstanceIdentityParse.Valid"/>
        /// with a lower-cased value, or <see cref="InstanceIdentityParse.Malformed"/>.
        ///
        /// <para>A header present more than once is <b>malformed</b>, not "take the first": two values
        /// mean the caller's intent is ambiguous, and picking one silently would let a caller smuggle a
        /// second identity past a reviewer reading only the first.</para>
        /// </summary>
        public static InstanceIdentityParse TryRead(IHeaderDictionary? headers, out string? instanceId)
        {
            instanceId = null;
            if (headers == null)
                return InstanceIdentityParse.Absent;

            if (!headers.TryGetValue(HeaderName, out var values))
                return InstanceIdentityParse.Absent;

            // Count == 0 happens for a header present with no value at all; that is malformed, not absent.
            if (values.Count == 0)
                return InstanceIdentityParse.Malformed;

            if (values.Count > 1)
                return InstanceIdentityParse.Malformed;

            return TryParse(values[0], out instanceId);
        }

        /// <summary>
        /// Validates a raw header value as an opaque instance id.
        ///
        /// <para>Note what is deliberately NOT done: the input is not trimmed and control characters are
        /// not stripped. A value with surrounding whitespace, an embedded CR/LF, or a NUL is
        /// <b>rejected</b>, not repaired. Repairing it would mean a client and the server disagree about
        /// what the identity is, which is precisely the class of bug that makes a replace rule evict the
        /// wrong session.</para>
        /// </summary>
        public static InstanceIdentityParse TryParse(string? raw, out string? instanceId)
        {
            instanceId = null;

            // A present-but-empty header value is malformed: the client asked to participate and sent
            // nothing. Treating it as Absent would be the silent downgrade this tri-state exists to stop.
            if (raw == null || raw.Length == 0)
                return InstanceIdentityParse.Malformed;

            if (raw.Length != ExpectedLength)
                return InstanceIdentityParse.Malformed;

            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return InstanceIdentityParse.Malformed;
            }

            instanceId = raw.ToLowerInvariant();
            return InstanceIdentityParse.Valid;
        }

        /// <summary>
        /// A short, non-reversible fingerprint of a validated instance id, for log correlation.
        ///
        /// <para><b>The raw instance id is never logged.</b> Operators debugging a reconnect flap need to
        /// tell two installations apart; they do not need the identifier itself. This returns the first 8
        /// hex chars of <c>SHA-256(instanceId)</c> — 32 bits, deliberately collision-prone, enough to
        /// correlate two log lines from one process and not enough to be a durable identifier in a log
        /// aggregator. Pass only values that already parsed <see cref="InstanceIdentityParse.Valid"/>.</para>
        /// </summary>
        public static string Fingerprint(string? instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
                return "none";
#if NET5_0_OR_GREATER
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(instanceId!));
#else
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.ASCII.GetBytes(instanceId!));
#endif
            var sb = new StringBuilder(8);
            for (var i = 0; i < 4; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
