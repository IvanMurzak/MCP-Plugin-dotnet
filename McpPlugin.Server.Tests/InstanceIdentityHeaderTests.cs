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
using System.Linq;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The opaque-token contract for <c>AI-Game-Dev-Instance-Id</c> (design 07 §2.3, D10.2).
    ///
    /// <para>Everything here exists because the value is <b>client-supplied and untrusted</b>. It is
    /// validated to a fixed length over a closed charset BEFORE it is used anywhere, which is what makes
    /// every downstream use safe by construction instead of safe by remembering to escape.</para>
    ///
    /// <para><b>The tri-state is the point.</b> ABSENT and MALFORMED must not collapse into each other:
    /// absent is every currently released client and must behave exactly as it does today, while malformed
    /// is a client that believes it is participating in the replace rule and would otherwise silently
    /// stack sessions forever.</para>
    /// </summary>
    public sealed class InstanceIdentityHeaderTests
    {
        // A well-formed value: 128 bits of HMAC output, hex ⇒ exactly 32 chars.
        const string ValidId = "0123456789abcdef0123456789abcdef";

        [Fact]
        public void HeaderName_IsExactlyTheContractedName_WithNoXPrefix()
        {
            // DoD 9 / RFC 6648. This is pinned as a test rather than trusted to review because the name is
            // half of a cross-repository contract: the app (W3.3) must send this byte-for-byte, and a
            // mismatch fails OPEN — the header is simply not seen, no replace happens, and the only symptom
            // is the six-hour session pile-up the rule exists to end.
            InstanceIdentityHeader.HeaderName.ShouldBe("AI-Game-Dev-Instance-Id");
            InstanceIdentityHeader.HeaderName.ShouldBe(Consts.MCP.Server.Headers.InstanceId);
            InstanceIdentityHeader.HeaderName.StartsWith("X-", StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse("RFC 6648 deprecates the X- prefix, and the client does not send one");
        }

        [Fact]
        public void ExpectedLength_Is32_Because128BitsHexIs32Chars()
        {
            // 128 bits ⇒ 16 bytes ⇒ 32 hex chars. Pinned so a "let's allow 64 too" relaxation is a
            // deliberate, visible change rather than a quiet widening of an opaque-token contract.
            InstanceIdentityHeader.ExpectedLength.ShouldBe(32);
            ValidId.Length.ShouldBe(InstanceIdentityHeader.ExpectedLength);
        }

        // ───────────────────────────── accepted ─────────────────────────────

        [Fact]
        public void LowerCaseHex_IsValid_AndPreserved()
        {
            InstanceIdentityHeader.TryParse(ValidId, out var parsed).ShouldBe(InstanceIdentityParse.Valid);
            parsed.ShouldBe(ValidId);
        }

        [Fact]
        public void UpperCaseHex_IsValid_AndNormalisedToLowerCase()
        {
            // Normalisation matters for the KEY, not for politeness: "ABCD…" and "abcd…" are the same
            // installation, and an un-normalised key would give one installation two slots — so its two
            // sessions would never replace each other and the pile-up would survive for that client.
            InstanceIdentityHeader.TryParse(ValidId.ToUpperInvariant(), out var parsed)
                .ShouldBe(InstanceIdentityParse.Valid);
            parsed.ShouldBe(ValidId);
        }

        // ───────────────────────────── rejected ─────────────────────────────

        [Theory]
        // Over-length — one char over, and a full 64-char (256-bit) value that is otherwise perfect hex.
        [InlineData("0123456789abcdef0123456789abcdef0")]
        [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
        // Under-length: a TRUNCATED value is the dangerous near-miss, because it still looks like hex.
        [InlineData("0123456789abcdef0123456789abcde")]
        [InlineData("0")]
        // Non-hex charset, at the start, middle and end — 'g' is the first letter past 'f'.
        [InlineData("g123456789abcdef0123456789abcdef")]
        [InlineData("0123456789abcdefg123456789abcdef")]
        [InlineData("0123456789abcdef0123456789abcdeg")]
        // Structural characters that would matter if the value were ever concatenated into something.
        [InlineData("0123456789abcdef0123456789abcd..")]
        [InlineData("../123456789abcdef0123456789abcd")]
        [InlineData("0123456789abcdef0123456789ab'--")]
        // Whitespace padding is REJECTED, not trimmed: silently repairing it would make client and server
        // disagree about what the identity is, which is how a replace rule evicts the wrong session.
        [InlineData(" 123456789abcdef0123456789abcdef")]
        [InlineData("0123456789abcdef0123456789abcde ")]
        // Empty and whitespace-only.
        [InlineData("")]
        [InlineData("                                ")]
        public void MalformedValues_AreRejected(string raw)
        {
            InstanceIdentityHeader.TryParse(raw, out var parsed).ShouldBe(InstanceIdentityParse.Malformed);
            parsed.ShouldBeNull("a rejected value must never be handed onward in any repaired form");
        }

        [Theory]
        // CR/LF and friends in the arrangements a log-injection or response-splitting attempt would use.
        // EVERY case here is EXACTLY 32 characters long, so the length check cannot be what rejects it —
        // the charset check has to do the work. That padding discipline is the whole value of this Theory:
        // unpadded inputs would be rejected on length and would prove nothing at all about CR/LF handling,
        // and the test would stay green with the charset loop deleted.
        [InlineData("0123456789abcdef\r\n6789abcdef0123")]  // 16 + 2 + 14
        [InlineData("0123456789abcdef\n0123456789abcde")]    // 16 + 1 + 15
        [InlineData("0123456789abcdef\r0123456789abcde")]    // 16 + 1 + 15
        [InlineData("\r\n23456789abcdef0123456789abcdef")]  // 2 + 30, leading
        [InlineData("0123456789abcdef0123456789abcd\r\n")]  // 30 + 2, trailing
        [InlineData("0123456789abcdef\00123456789abcde")]    // NUL
        [InlineData("0123456789abcdef\t0123456789abcde")]    // TAB
        public void ControlCharacters_AreRejectedByCharsetNotByLength(string raw)
        {
            // The discriminator, asserted inline so it cannot rot: if a future edit changed one of these
            // inputs to the wrong length, this line fails loudly instead of the case quietly degrading into
            // a length test that happens to pass.
            raw.Length.ShouldBe(InstanceIdentityHeader.ExpectedLength,
                "this case must be rejected by the CHARSET check, so it must have the VALID length");

            InstanceIdentityHeader.TryParse(raw, out var parsed).ShouldBe(InstanceIdentityParse.Malformed);
            parsed.ShouldBeNull("a rejected value must never be handed onward in any repaired form");
        }

        [Fact]
        public void NullValue_IsMalformed_NotAbsent()
        {
            // TryParse is only reached when the header was PRESENT. A present header with a null value is a
            // client that asked to participate and sent nothing — malformed, not absent.
            InstanceIdentityHeader.TryParse(null, out var parsed).ShouldBe(InstanceIdentityParse.Malformed);
            parsed.ShouldBeNull();
        }

        // ───────────────────────────── header dictionary reads ─────────────────────────────

        [Fact]
        public void NoHeaderAtAll_IsAbsent_WhichIsTheReleasedClientPath()
        {
            var headers = new HeaderDictionary();
            InstanceIdentityHeader.TryRead(headers, out var parsed).ShouldBe(InstanceIdentityParse.Absent);
            parsed.ShouldBeNull();
        }

        [Fact]
        public void NullHeaderDictionary_IsAbsent()
        {
            InstanceIdentityHeader.TryRead(null, out var parsed).ShouldBe(InstanceIdentityParse.Absent);
            parsed.ShouldBeNull();
        }

        [Fact]
        public void HeaderPresentWithValidValue_IsValid_AndCaseInsensitiveOnTheNAME()
        {
            // HTTP header names are case-insensitive, and IHeaderDictionary honours that. Asserted so a
            // client that sends "ai-game-dev-instance-id" is not silently treated as sending nothing.
            var headers = new HeaderDictionary { { "ai-game-dev-instance-id", ValidId } };
            InstanceIdentityHeader.TryRead(headers, out var parsed).ShouldBe(InstanceIdentityParse.Valid);
            parsed.ShouldBe(ValidId);
        }

        [Fact]
        public void HeaderSentTwice_IsMalformed_NotFirstWins()
        {
            // Two values mean the caller's intent is ambiguous. Taking the first would let a caller smuggle
            // a second identity past anyone reading only the first, and would make which session gets
            // evicted depend on header ordering.
            var headers = new HeaderDictionary
            {
                { InstanceIdentityHeader.HeaderName, new Microsoft.Extensions.Primitives.StringValues(new[] { ValidId, "fedcba9876543210fedcba9876543210" }) }
            };
            InstanceIdentityHeader.TryRead(headers, out var parsed).ShouldBe(InstanceIdentityParse.Malformed);
            parsed.ShouldBeNull();
        }

        [Fact]
        public void HeaderPresentWithEmptyValue_IsMalformed()
        {
            var headers = new HeaderDictionary { { InstanceIdentityHeader.HeaderName, string.Empty } };
            InstanceIdentityHeader.TryRead(headers, out var parsed).ShouldBe(InstanceIdentityParse.Malformed);
            parsed.ShouldBeNull();
        }

        // ───────────────────────────── fingerprint ─────────────────────────────

        [Fact]
        public void Fingerprint_NeverReturnsTheValue_AndIsShortHex()
        {
            var fingerprint = InstanceIdentityHeader.Fingerprint(ValidId);

            fingerprint.ShouldNotBe(ValidId, "the log-safe form must not be the identifier itself");
            ValidId.Contains(fingerprint, StringComparison.Ordinal).ShouldBeFalse(
                "the fingerprint must not be a substring of the id — a prefix would leak the id's own bytes");
            fingerprint.Length.ShouldBe(8);
            fingerprint.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).ShouldBeTrue();
        }

        [Fact]
        public void Fingerprint_IsStableForOneIdAndDiffersAcrossIds()
        {
            // Both halves matter: stable makes it useful for correlating two log lines about one
            // installation; different makes it useful for telling two installations apart. A constant would
            // pass "stable" alone and be worthless.
            InstanceIdentityHeader.Fingerprint(ValidId).ShouldBe(InstanceIdentityHeader.Fingerprint(ValidId));
            InstanceIdentityHeader.Fingerprint(ValidId)
                .ShouldNotBe(InstanceIdentityHeader.Fingerprint("fedcba9876543210fedcba9876543210"));
        }

        [Fact]
        public void Fingerprint_OfNothing_IsAPlaceholder_NotAnException()
        {
            // The log path must never be able to throw: a logging failure inside a session teardown would
            // turn a clean replace into a fault.
            InstanceIdentityHeader.Fingerprint(null).ShouldBe("none");
            InstanceIdentityHeader.Fingerprint(string.Empty).ShouldBe("none");
        }
    }
}
