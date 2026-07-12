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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    /// <summary>
    /// Test-only helpers for minting ES256 (and adversarial) JWTs and the matching JWKS document,
    /// so the resource-server validation fuzz suite runs fully hermetically (no network, no external
    /// identity library).
    /// </summary>
    internal static class TestJwt
    {
        public static ECDsa CreateKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public static string Base64UrlEncode(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string Base64UrlEncode(string text)
            => Base64UrlEncode(Encoding.UTF8.GetBytes(text));

        /// <summary>Build a JWKS JSON document exposing the public part of <paramref name="key"/> under <paramref name="kid"/>.</summary>
        public static string BuildJwks(ECDsa key, string kid)
        {
            var p = key.ExportParameters(false);
            var jwk = new Dictionary<string, object>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["use"] = "sig",
                ["alg"] = "ES256",
                ["kid"] = kid,
                ["x"] = Base64UrlEncode(p.Q.X!),
                ["y"] = Base64UrlEncode(p.Q.Y!)
            };
            var set = new Dictionary<string, object> { ["keys"] = new[] { jwk } };
            return JsonSerializer.Serialize(set);
        }

        /// <summary>Mint a signed ES256 JWT with the given claims.</summary>
        public static string SignEs256(ECDsa key, string kid, IDictionary<string, object> claims)
        {
            var header = new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "JWT", ["kid"] = kid };
            var headerB64 = Base64UrlEncode(JsonSerializer.Serialize(header));
            var payloadB64 = Base64UrlEncode(JsonSerializer.Serialize(claims));
            var signingInput = Encoding.ASCII.GetBytes(headerB64 + "." + payloadB64);
            var sig = key.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return headerB64 + "." + payloadB64 + "." + Base64UrlEncode(sig);
        }

        /// <summary>Mint a token with a caller-chosen <c>alg</c> header, signed with HMAC-SHA256 over the given secret (alg-confusion attack vector).</summary>
        public static string SignHs256(string alg, string kid, IDictionary<string, object> claims, byte[] hmacSecret)
        {
            var header = new Dictionary<string, object> { ["alg"] = alg, ["typ"] = "JWT", ["kid"] = kid };
            var headerB64 = Base64UrlEncode(JsonSerializer.Serialize(header));
            var payloadB64 = Base64UrlEncode(JsonSerializer.Serialize(claims));
            var signingInput = Encoding.ASCII.GetBytes(headerB64 + "." + payloadB64);
            using var hmac = new HMACSHA256(hmacSecret);
            var sig = hmac.ComputeHash(signingInput);
            return headerB64 + "." + payloadB64 + "." + Base64UrlEncode(sig);
        }

        /// <summary>Mint an <c>alg:none</c> token whose signature segment is present-but-bogus (so it parses as a JWT).</summary>
        public static string BuildAlgNone(string kid, IDictionary<string, object> claims)
        {
            var header = new Dictionary<string, object> { ["alg"] = "none", ["typ"] = "JWT", ["kid"] = kid };
            var headerB64 = Base64UrlEncode(JsonSerializer.Serialize(header));
            var payloadB64 = Base64UrlEncode(JsonSerializer.Serialize(claims));
            return headerB64 + "." + payloadB64 + "." + Base64UrlEncode(Encoding.ASCII.GetBytes("not-a-signature"));
        }

        /// <summary>The SPKI-encoded public key bytes — a realistic secret an HS256 key-confusion attack would use.</summary>
        public static byte[] PublicKeyBytes(ECDsa key) => key.ExportSubjectPublicKeyInfo();

        public static long Unix(DateTimeOffset time) => time.ToUnixTimeSeconds();

        /// <summary>Standard MCP agent claim set; override individual entries as needed.</summary>
        public static Dictionary<string, object> Claims(
            string iss, string aud, DateTimeOffset exp, string sub = "user-123", string scope = "mcp:agent",
            DateTimeOffset? nbf = null, DateTimeOffset? iat = null)
        {
            var claims = new Dictionary<string, object>
            {
                ["iss"] = iss,
                ["aud"] = aud,
                ["sub"] = sub,
                ["scope"] = scope,
                ["exp"] = Unix(exp)
            };
            if (nbf.HasValue) claims["nbf"] = Unix(nbf.Value);
            if (iat.HasValue) claims["iat"] = Unix(iat.Value);
            return claims;
        }
    }
}
