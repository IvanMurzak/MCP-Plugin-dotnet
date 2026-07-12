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

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Base64url (RFC 4648 §5, no padding) decode used for JWS/JWT and JWK material. Hand-rolled to
    /// keep the resource-server validation self-contained (no external identity-model dependency)
    /// and portable across net8.0 / net9.0.
    /// </summary>
    public static class Base64Url
    {
        /// <summary>
        /// Decode a base64url string to bytes. Returns <c>false</c> for any malformed input
        /// (invalid characters, wrong length) rather than throwing — callers fail closed.
        /// </summary>
        public static bool TryDecode(string? input, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (input == null)
                return false;

            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 0: break;
                case 2: s += "=="; break;
                case 3: s += "="; break;
                default: return false; // length % 4 == 1 is never valid base64
            }

            try
            {
                bytes = Convert.FromBase64String(s);
                return true;
            }
            catch (FormatException)
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }
    }
}
