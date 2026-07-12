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
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Server.Auth.OAuth;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.OAuth
{
    /// <summary>
    /// Resource-server validation fuzz suite (mcp-authorize b2 DoD): expired / bad-iss / bad-aud /
    /// unknown-kid / alg-confusion (alg:none, HS256-with-the-public-key) are all rejected; the skew
    /// edges are covered; loopback-aliased and array audiences are accepted; the opaque/introspection
    /// path is exercised.
    /// </summary>
    public class AccessTokenValidatorTests
    {
        const string Issuer = "https://as.example";
        const string Resource = "http://localhost:23471";
        const string Kid = "key-1";
        static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        static OAuthResourceServerConfig Config() => new OAuthResourceServerConfig(Issuer, Resource);

        static AccessTokenValidator Validator(ECDsa signingKey, IIntrospectionClient? introspection = null)
        {
            var jwks = new FakeJwksKeyProvider().Add(Kid, signingKey);
            return new AccessTokenValidator(Config(), jwks, introspection ?? FakeIntrospectionClient.AlwaysInactive, () => Now);
        }

        static Task<OAuthValidationResult> Validate(AccessTokenValidator v, string token)
            => v.ValidateAsync(token, CancellationToken.None);

        [Fact]
        public async Task ValidEs256Token_Succeeds()
        {
            using var key = TestJwt.CreateKey();
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
            result.Subject.ShouldBe("user-123");
            result.Scope.ShouldBe("mcp:agent");
            result.TokenType.ShouldBe("jwt");
        }

        [Fact]
        public async Task ExpiredToken_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddMinutes(-10)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("expired");
        }

        [Fact]
        public async Task BadIssuer_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims("https://evil.example", Resource, Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("issuer");
        }

        [Fact]
        public async Task BadAudience_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, "http://localhost:59999", Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("audience");
        }

        [Fact]
        public async Task UnknownKid_Rejected()
        {
            using var key = TestJwt.CreateKey();
            // Signed with a kid the provider does not know.
            var token = TestJwt.SignEs256(key, "some-other-kid", TestJwt.Claims(Issuer, Resource, Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("kid");
        }

        [Fact]
        public async Task AlgNone_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var token = TestJwt.BuildAlgNone(Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("alg");
        }

        [Fact]
        public async Task Hs256SignedWithPublicKey_Rejected()
        {
            using var key = TestJwt.CreateKey();
            // Classic alg-confusion: attacker HMACs with the (public) verification key as the secret.
            var token = TestJwt.SignHs256("HS256", Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1)), TestJwt.PublicKeyBytes(key));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("alg");
        }

        [Fact]
        public async Task WrongSignature_SameKid_Rejected()
        {
            using var trustedKey = TestJwt.CreateKey();
            using var attackerKey = TestJwt.CreateKey();
            // Provider trusts trustedKey under Kid; token is signed by attackerKey but claims Kid.
            var token = TestJwt.SignEs256(attackerKey, Kid, TestJwt.Claims(Issuer, Resource, Now.AddHours(1)));

            var result = await Validate(Validator(trustedKey), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("signature");
        }

        [Fact]
        public async Task Expiry_WithinSkew_Accepted()
        {
            using var key = TestJwt.CreateKey();
            // 4 min in the past — inside the ±5 min skew tolerance.
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddMinutes(-4)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
        }

        [Fact]
        public async Task Expiry_BeyondSkew_Rejected()
        {
            using var key = TestJwt.CreateKey();
            // 6 min in the past — beyond the ±5 min skew tolerance.
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, Resource, Now.AddMinutes(-6)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("expired");
        }

        [Fact]
        public async Task NotBefore_WithinSkew_Accepted()
        {
            using var key = TestJwt.CreateKey();
            var claims = TestJwt.Claims(Issuer, Resource, Now.AddHours(1), nbf: Now.AddMinutes(4));
            var token = TestJwt.SignEs256(key, Kid, claims);

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
        }

        [Fact]
        public async Task NotBefore_BeyondSkew_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var claims = TestJwt.Claims(Issuer, Resource, Now.AddHours(1), nbf: Now.AddMinutes(6));
            var token = TestJwt.SignEs256(key, Kid, claims);

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("not yet valid");
        }

        [Fact]
        public async Task LoopbackAliasedAudience_Accepted()
        {
            using var key = TestJwt.CreateKey();
            // aud uses 127.0.0.1, --public-url uses localhost — must match after normalization.
            var token = TestJwt.SignEs256(key, Kid, TestJwt.Claims(Issuer, "http://127.0.0.1:23471", Now.AddHours(1)));

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
        }

        [Fact]
        public async Task ArrayAudienceContainingResource_Accepted()
        {
            using var key = TestJwt.CreateKey();
            var claims = TestJwt.Claims(Issuer, Resource, Now.AddHours(1));
            claims["aud"] = new[] { "https://other.example", Resource };
            var token = TestJwt.SignEs256(key, Kid, claims);

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeTrue(result.FailureReason);
        }

        [Fact]
        public async Task MissingExp_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var claims = new Dictionary<string, object>
            {
                ["iss"] = Issuer,
                ["aud"] = Resource,
                ["sub"] = "user-123",
                ["scope"] = "mcp:agent"
            };
            var token = TestJwt.SignEs256(key, Kid, claims);

            var result = await Validate(Validator(key), token);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("exp");
        }

        // ── Opaque token path (introspection) ─────────────────────────────────────────────────────

        [Fact]
        public async Task OpaqueToken_ActiveIntrospection_Succeeds()
        {
            using var key = TestJwt.CreateKey();
            var introspection = new FakeIntrospectionClient(t =>
                t == "agd_pat_abc" ? new IntrospectionResult(true, "pat-user", "mcp:agent", Now.AddHours(1)) : IntrospectionResult.Inactive);

            var result = await Validate(Validator(key, introspection), "agd_pat_abc");

            result.Succeeded.ShouldBeTrue(result.FailureReason);
            result.Subject.ShouldBe("pat-user");
            result.TokenType.ShouldBe("opaque");
        }

        [Fact]
        public async Task OpaqueToken_InactiveIntrospection_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var result = await Validate(Validator(key, FakeIntrospectionClient.AlwaysInactive), "agd_pat_unknown");

            result.Succeeded.ShouldBeFalse();
            result.TokenType.ShouldBe("opaque");
        }

        [Fact]
        public async Task OpaqueToken_ActiveButExpired_Rejected()
        {
            using var key = TestJwt.CreateKey();
            var introspection = new FakeIntrospectionClient(_ =>
                new IntrospectionResult(true, "pat-user", "mcp:agent", Now.AddMinutes(-10)));

            var result = await Validate(Validator(key, introspection), "agd_pat_stale");

            result.Succeeded.ShouldBeFalse();
            result.FailureReason!.ShouldContain("expired");
        }
    }
}
