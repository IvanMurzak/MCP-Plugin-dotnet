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
using System.Text.Json;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Minimal JWKS (RFC 7517) reader for EC P-256 signing keys (mcp-authorize b2). Parses the
    /// authorization server's <c>jwks.json</c> and resolves a <c>kid</c> to an <see cref="ECDsa"/>
    /// public key for ES256 verification. Non-EC / non-P-256 keys are ignored — the RS accepts
    /// ES256 only, so unsupported key types simply do not resolve (fail closed).
    /// </summary>
    public sealed class JsonWebKeySet
    {
        private readonly Dictionary<string, ECParameters> _keysByKid;

        private JsonWebKeySet(Dictionary<string, ECParameters> keysByKid)
        {
            _keysByKid = keysByKid;
        }

        /// <summary>The set of <c>kid</c>s present in this key set.</summary>
        public IReadOnlyCollection<string> KeyIds => _keysByKid.Keys;

        /// <summary>True when a key with the given <c>kid</c> is present.</summary>
        public bool ContainsKid(string kid) => _keysByKid.ContainsKey(kid);

        /// <summary>
        /// Create a fresh <see cref="ECDsa"/> for the given <c>kid</c>, or <c>null</c> when absent.
        /// The caller owns the returned instance and should dispose it.
        /// </summary>
        public ECDsa? CreateEcdsa(string? kid)
        {
            if (kid == null || !_keysByKid.TryGetValue(kid, out var parameters))
                return null;
            try
            {
                return ECDsa.Create(parameters);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException)
            {
                // Invalid key material (point not on the curve, bad coordinates) → fail closed as an
                // unknown key rather than throwing out of the validator (the RS "never throws").
                return null;
            }
        }

        /// <summary>
        /// Parse a JWKS document. Returns <c>false</c> (fail closed) for malformed JSON. A valid
        /// document with zero usable EC/P-256 keys still returns <c>true</c> with an empty set.
        /// </summary>
        public static bool TryParse(string? json, out JsonWebKeySet keySet)
        {
            keySet = new JsonWebKeySet(new Dictionary<string, ECParameters>(StringComparer.Ordinal));
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(json!);
                if (!doc.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                    return false;

                var map = new Dictionary<string, ECParameters>(StringComparer.Ordinal);
                foreach (var key in keys.EnumerateArray())
                {
                    if (!TryReadEcKey(key, out var kid, out var parameters))
                        continue;
                    map[kid] = parameters;
                }

                keySet = new JsonWebKeySet(map);
                return true;
            }
            catch (JsonException)
            {
                keySet = new JsonWebKeySet(new Dictionary<string, ECParameters>(StringComparer.Ordinal));
                return false;
            }
        }

        private static bool TryReadEcKey(JsonElement key, out string kid, out ECParameters parameters)
        {
            kid = string.Empty;
            parameters = default;

            if (key.ValueKind != JsonValueKind.Object)
                return false;

            if (!TryGetString(key, "kty", out var kty) || kty != "EC")
                return false;
            if (!TryGetString(key, "crv", out var crv) || crv != "P-256")
                return false;
            if (!TryGetString(key, "kid", out var keyId) || string.IsNullOrEmpty(keyId))
                return false;
            if (!TryGetString(key, "x", out var x) || !Base64Url.TryDecode(x, out var xb))
                return false;
            if (!TryGetString(key, "y", out var y) || !Base64Url.TryDecode(y, out var yb))
                return false;

            // P-256 field elements are exactly 32 bytes; reject any mis-sized coordinate (fail closed)
            // so a malformed JWK never reaches ECDsa.Create with an invalid point.
            if (xb.Length != 32 || yb.Length != 32)
                return false;

            // Reject a key advertised for anything other than signature verification.
            if (TryGetString(key, "use", out var use) && use != "sig")
                return false;

            kid = keyId;
            parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = xb, Y = yb }
            };
            return true;
        }

        private static bool TryGetString(JsonElement obj, string name, out string value)
        {
            value = string.Empty;
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString() ?? string.Empty;
                return true;
            }
            return false;
        }
    }
}
