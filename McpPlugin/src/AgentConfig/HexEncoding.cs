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
    /// Lowercase hex encoding shared by every hash- or nonce-derived string this package writes
    /// (routing pins, lock nonces, the dead-family memo's token hash). Lowercase is part of the
    /// cross-language contracts: the TS twins use Node's <c>digest("hex")</c>, which is lowercase.
    /// </summary>
    internal static class HexEncoding
    {
        /// <summary>The first <paramref name="count"/> bytes of <paramref name="bytes"/> as lowercase hex.</summary>
        internal static string ToLowerHex(byte[] bytes, int count)
        {
            var sb = new StringBuilder(count * 2);
            for (var i = 0; i < count; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>All of <paramref name="bytes"/> as lowercase hex.</summary>
        internal static string ToLowerHex(byte[] bytes) => ToLowerHex(bytes, bytes.Length);

        /// <summary>
        /// Lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="text"/> — Node's
        /// <c>createHash("sha256").update(text, "utf8").digest("hex")</c>.
        /// </summary>
        internal static string Sha256Hex(string text)
        {
            using var sha = SHA256.Create();
            return ToLowerHex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
    }
}
