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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin.Server.Auth.OAuth
{
    /// <summary>
    /// Default <see cref="IOAuthTokenValidator"/> (mcp-authorize b2). Validates:
    /// <list type="bullet">
    ///   <item><b>ES256 JWTs</b> — header <c>alg</c> MUST be exactly <c>ES256</c> (this alone
    ///         defeats the <c>alg:none</c> and HS256-with-the-public-key key-confusion attacks:
    ///         no symmetric or "none" path is ever taken and no key material is loaded before the
    ///         algorithm is confirmed); signature verified with the JWKS P-256 key resolved by
    ///         <c>kid</c>; strict <c>iss</c>, strict per-resource <c>aud</c> (loopback-aliased),
    ///         and <c>exp</c>/<c>nbf</c> with ±5 min skew.</item>
    ///   <item><b>Opaque tokens</b> — routed to introspection (fail-closed).</item>
    /// </list>
    /// Every failure path returns <see cref="OAuthValidationResult.Fail"/>; nothing throws.
    /// </summary>
    public sealed class AccessTokenValidator : IOAuthTokenValidator
    {
        private const string RequiredAlgorithm = "ES256";
        private const int P256SignatureLength = 64; // r(32) || s(32), IEEE P1363

        private readonly OAuthResourceServerConfig _config;
        private readonly IJwksKeyProvider _jwks;
        private readonly IIntrospectionClient _introspection;
        private readonly Func<DateTimeOffset> _now;
        private readonly ILogger? _logger;

        public AccessTokenValidator(
            OAuthResourceServerConfig config,
            IJwksKeyProvider jwks,
            IIntrospectionClient introspection,
            Func<DateTimeOffset>? now = null,
            ILogger? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _jwks = jwks ?? throw new ArgumentNullException(nameof(jwks));
            _introspection = introspection ?? throw new ArgumentNullException(nameof(introspection));
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _logger = logger;
        }

        public Task<OAuthValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Task.FromResult(OAuthValidationResult.Fail("unknown", "empty token"));

            return LooksLikeJwt(token, out var header, out var payload, out var signature)
                ? ValidateJwtAsync(header, payload, signature, cancellationToken)
                : ValidateOpaqueAsync(token, cancellationToken);
        }

        // A JWT is exactly three non-empty base64url segments whose header decodes to a JSON object
        // carrying "alg". Anything else (e.g. an opaque `agd_pat_…`) is routed to introspection.
        private static bool LooksLikeJwt(string token, out string header, out string payload, out string signature)
        {
            header = payload = signature = string.Empty;
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
                return false;
            if (!Base64Url.TryDecode(parts[0], out var headerBytes))
                return false;
            try
            {
                using var doc = JsonDocument.Parse(headerBytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("alg", out _))
                    return false;
            }
            catch (JsonException)
            {
                return false;
            }

            header = parts[0];
            payload = parts[1];
            signature = parts[2];
            return true;
        }

        private async Task<OAuthValidationResult> ValidateJwtAsync(string headerSeg, string payloadSeg, string signatureSeg, CancellationToken cancellationToken)
        {
            // ---- Header: enforce ES256 BEFORE touching any key material (alg-confusion defense) --
            if (!Base64Url.TryDecode(headerSeg, out var headerBytes))
                return OAuthValidationResult.Fail("jwt", "malformed header");

            string alg;
            string? kid;
            try
            {
                using var headerDoc = JsonDocument.Parse(headerBytes);
                alg = GetString(headerDoc.RootElement, "alg") ?? string.Empty;
                kid = GetString(headerDoc.RootElement, "kid");
            }
            catch (JsonException)
            {
                return OAuthValidationResult.Fail("jwt", "malformed header");
            }

            if (!string.Equals(alg, RequiredAlgorithm, StringComparison.Ordinal))
                return OAuthValidationResult.Fail("jwt", $"unsupported alg '{alg}' (only ES256 accepted)");
            if (string.IsNullOrEmpty(kid))
                return OAuthValidationResult.Fail("jwt", "missing kid");

            // ---- Signature: verify over the received header.payload with the JWKS P-256 key -------
            if (!Base64Url.TryDecode(signatureSeg, out var signature) || signature.Length != P256SignatureLength)
                return OAuthValidationResult.Fail("jwt", "malformed signature");

            using var ecdsa = await _jwks.GetSigningKeyAsync(kid!, cancellationToken).ConfigureAwait(false);
            if (ecdsa == null)
                return OAuthValidationResult.Fail("jwt", "unknown signing key (kid)");

            var signingInput = Encoding.ASCII.GetBytes(headerSeg + "." + payloadSeg);
            bool signatureValid;
            try
            {
                signatureValid = ecdsa.VerifyData(
                    signingInput, signature, HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (CryptographicException ex)
            {
                _logger?.LogWarning(ex, "ES256 signature verification threw; rejecting token.");
                return OAuthValidationResult.Fail("jwt", "signature verification failed");
            }

            if (!signatureValid)
                return OAuthValidationResult.Fail("jwt", "invalid signature");

            // ---- Claims: iss / aud / exp / nbf ----------------------------------------------------
            if (!Base64Url.TryDecode(payloadSeg, out var payloadBytes))
                return OAuthValidationResult.Fail("jwt", "malformed payload");

            try
            {
                using var payloadDoc = JsonDocument.Parse(payloadBytes);
                var claims = payloadDoc.RootElement;

                var iss = GetString(claims, "iss");
                if (!string.Equals(iss, _config.Issuer, StringComparison.Ordinal))
                    return OAuthValidationResult.Fail("jwt", "issuer mismatch");

                if (!AudienceContainsResource(claims))
                    return OAuthValidationResult.Fail("jwt", "audience mismatch");

                var now = _now();

                if (!TryGetUnixSeconds(claims, "exp", out var exp))
                    return OAuthValidationResult.Fail("jwt", "missing exp");
                if (now > exp + _config.ClockSkew)
                    return OAuthValidationResult.Fail("jwt", "token expired");

                if (TryGetUnixSeconds(claims, "nbf", out var nbf) && now < nbf - _config.ClockSkew)
                    return OAuthValidationResult.Fail("jwt", "token not yet valid");

                var sub = GetString(claims, "sub");
                var scope = GetString(claims, "scope");
                var clientId = GetString(claims, "client_id");
                return OAuthValidationResult.Success("jwt", sub, scope, clientId);
            }
            catch (JsonException)
            {
                return OAuthValidationResult.Fail("jwt", "malformed payload");
            }
        }

        private async Task<OAuthValidationResult> ValidateOpaqueAsync(string token, CancellationToken cancellationToken)
        {
            var result = await _introspection.IntrospectAsync(token, cancellationToken).ConfigureAwait(false);
            if (!result.Active)
                return OAuthValidationResult.Fail("opaque", "inactive token");

            if (result.ExpiresAt is DateTimeOffset exp && _now() > exp + _config.ClockSkew)
                return OAuthValidationResult.Fail("opaque", "token expired");

            return OAuthValidationResult.Success("opaque", result.Subject, result.Scope);
        }

        private bool AudienceContainsResource(JsonElement claims)
        {
            if (!claims.TryGetProperty("aud", out var aud))
                return false;

            if (aud.ValueKind == JsonValueKind.String)
                return UrlNormalization.ResourcesMatch(aud.GetString(), _config.ResourceUrl);

            if (aud.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in aud.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String
                        && UrlNormalization.ResourcesMatch(entry.GetString(), _config.ResourceUrl))
                        return true;
                }
            }

            return false;
        }

        private static string? GetString(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;

        private static bool TryGetUnixSeconds(JsonElement obj, string name, out DateTimeOffset value)
        {
            value = default;
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var seconds))
            {
                value = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return true;
            }
            return false;
        }
    }
}
