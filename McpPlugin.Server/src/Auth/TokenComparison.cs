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

namespace com.IvanMurzak.McpPlugin.Server.Auth
{
    /// <summary>
    /// Constant-time secret comparison for the offline <c>token</c> auth mode (mcp-authorize g6).
    /// <para>
    /// Both the presented and expected secrets are reduced to a fixed 32-byte SHA-256 digest, then
    /// compared with <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>.
    /// Hashing to a fixed length is deliberate: it removes the length side channel a raw
    /// <c>FixedTimeEquals</c> over the utf-8 bytes would still leak (the method short-circuits on a
    /// length mismatch), and it upgrades the security posture over the deleted b5 code, which compared
    /// with <c>string.Equals(StringComparison.Ordinal)</c> — an early-exit, timing-leaky comparison.
    /// </para>
    /// </summary>
    public static class TokenComparison
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="presented"/> equals <paramref name="expected"/> in
        /// constant time relative to the secret's content. A null/empty operand never matches (the
        /// server never runs the token strategy without a configured secret).
        /// </summary>
        public static bool FixedTimeEquals(string? presented, string? expected)
        {
            if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected))
                return false;

            Span<byte> presentedDigest = stackalloc byte[32];
            Span<byte> expectedDigest = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(presented), presentedDigest);
            SHA256.HashData(Encoding.UTF8.GetBytes(expected), expectedDigest);
            return CryptographicOperations.FixedTimeEquals(presentedDigest, expectedDigest);
        }
    }
}
