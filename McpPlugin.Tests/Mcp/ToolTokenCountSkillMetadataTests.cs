/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    /// <summary>
    /// Pins that the catalogue-weight instrument measures the skill metadata that <c>tools/list</c>
    /// actually ships (<c>_meta.skillDescription</c> / <c>_meta.skillBody</c>), and that the total's
    /// meaning is unchanged for a tool that publishes none.
    ///
    /// <para>
    /// Every expectation here is an exact value, never a bound: the counter has a "no skill metadata"
    /// branch that satisfies any <c>&gt; 0</c> or <c>&gt;=</c> assertion for free, so only an exact
    /// figure can tell a measured payload from an unmeasured one.
    /// </para>
    /// </summary>
    [Collection("McpPlugin")]
    public class ToolTokenCountSkillMetadataTests
    {
        private readonly Version _version = new Version();

        // 41 characters. Plain ASCII on purpose: System.Text.Json escapes '<', '>', '&', '+', '\'' and
        // every non-ASCII code point, which would make the measured length differ from the literal length.
        private const string SkillDescriptionText = "Concise skill blurb for the tool catalog.";

        // 70 characters, same ASCII-only rule.
        private const string SkillBodyText = "Long form skill markdown body shipped out to any connected MCP client.";

        // 30 characters. A SECOND, deliberately DIFFERENT-SIZED payload: it adds 22 + 30 = 52 characters,
        // also a multiple of 4, so its share is a base-independent 13. Two unequal shares are what make the
        // catalogue-level assertion pin an ADDITION — with a single non-zero contributor, sum / max / first
        // over the enabled set are indistinguishable.
        private const string SecondSkillDescriptionText = "A second, smaller skill blurb.";

        private const int SecondSkillMetadataTokens = 13;

        // A value no Calculate over any fixture in this file can produce, used to prove a property is served
        // FROM its cache field rather than recomputed on every read.
        private const int CacheSentinel = 123456;

        // RunTool and ProxyTool name their cache fields identically, which is what lets one pair of literals
        // drive both implementations' tests. CachedField asserts the field resolves, so a rename in either
        // production type reddens here rather than passing silently.
        private const string TotalCacheField = "_cachedTokenCount";
        private const string ShareCacheField = "_cachedSkillMetadataTokenCount";

        /// <summary>
        /// Characters the two members add to the measured JSON:
        /// <c>,"skillDescription":"…"</c> = 22 + 41 and <c>,"skillBody":"…"</c> = 15 + 70, i.e. 148.
        /// 148 is a multiple of 4, which is why the token delta is EXACTLY this value regardless of how
        /// long the rest of the entry is — that base-independence is what lets the reflection-based
        /// <see cref="RunTool"/> assertions below pin an exact delta over a schema this test does not author.
        /// </summary>
        private const int SkillMetadataTokens = 37;

        // Fixture for the direct-helper assertions: every input is fixed, so every count is a literal.
        private const string ProbeName = "catalogue_probe";
        private const string ProbeTitle = "Catalogue Probe";
        private const string ProbeDescription = "Measures the catalogue weight of one tool entry.";
        private const string ProbeInputSchemaJson = "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}}}";

        // Measured counts for that fixture (chars / 4, rounded up).
        private const int ProbeTokensWithoutSkillMetadata = 48;   // 190 chars
        private const int ProbeTokensWithBothMembers = 85;        // 338 chars
        private const int ProbeTokensWithSkillDescriptionOnly = 64;  // 253 chars
        private const int ProbeTokensWithSkillBodyOnly = 69;         // 275 chars

        private static JsonNode ProbeInputSchema() => JsonNode.Parse(ProbeInputSchemaJson)!;

        private static Func<string, IReadOnlyDictionary<string, JsonElement>?, CancellationToken, Task<ResponseCallTool>> NoopHandler()
            => (_, _, _) => Task.FromResult(ResponseCallTool.Success("noop"));

        // ── The helper ──────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Calculate_WithoutSkillMetadata_PinsThePreChangeBaselineExactly()
        {
            var nulls = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, null, null, ProbeInputSchema(), null);

            // Empty strings must cost exactly what null costs — the same string.IsNullOrEmpty accounting
            // the name/title/description members already get, and the same gate the wire uses.
            var empties = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, string.Empty, string.Empty, ProbeInputSchema(), null);

            nulls.ShouldBe(ProbeTokensWithoutSkillMetadata, "a NULL skill member must cost exactly nothing");
            empties.ShouldBe(ProbeTokensWithoutSkillMetadata, "an EMPTY-STRING skill member must cost exactly nothing");
        }

        [Fact]
        public void Calculate_WithSkillMetadata_AddsExactlyTheMeasuredPayload()
        {
            var withMetadata = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, SkillBodyText, ProbeInputSchema(), null);

            // Only the literal, never `withMetadata - ProbeTokensWithoutSkillMetadata == SkillMetadataTokens`:
            // those three are compile-time constants (85 - 48 == 37), so that form is entailed by the line
            // below and cannot fail independently of it. The delta claim has its own test underneath.
            withMetadata.ShouldBe(ProbeTokensWithBothMembers);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Calculate_TheSkillMetadataDelta_IsIndependentOfTheBaseLength(int extraBaseCharacters)
        {
            // The payload is 148 characters — a multiple of the 4-chars-per-token divisor — so the delta is
            // the same 37 tokens whatever the rest of the entry weighs. One fixture cannot show that, and the
            // gap is not academic: with a single base length, a payload 1-3 characters off (a mis-spelled
            // wire key, an added separator) still rounds to the same 37 and every other test here stays green.
            // Padding the description by 0-3 characters walks the base length through all four residues
            // modulo that divisor, which is a property of the divisor and the fixture — not of the code
            // under test — and at least one residue reddens for any payload that is not exactly 148.
            var description = ProbeDescription + new string('.', extraBaseCharacters);

            var without = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, description, null, null, ProbeInputSchema(), null);
            var with = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, description, SkillDescriptionText, SkillBodyText, ProbeInputSchema(), null);

            (with - without).ShouldBe(SkillMetadataTokens, $"base padded by {extraBaseCharacters} character(s)");
        }

        [Fact]
        public void Calculate_WithSkillDescriptionOnly_AddsExactlyThatMembersPayload()
        {
            var value = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, null, ProbeInputSchema(), null);

            value.ShouldBe(ProbeTokensWithSkillDescriptionOnly);
        }

        [Fact]
        public void Calculate_WithSkillBodyOnly_AddsExactlyThatMembersPayload()
        {
            var value = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, null, SkillBodyText, ProbeInputSchema(), null);

            value.ShouldBe(ProbeTokensWithSkillBodyOnly);
        }

        [Fact]
        public void Calculate_MeasuresTheSkillMembersUnderTheirPublishedWireKeys()
        {
            // The instrument is only meaningful if it measures the payload the wire ships, so the keys it
            // serializes under are part of the contract, not an implementation detail.
            ToolTokenCount.SkillDescriptionKey.ShouldBe("skillDescription");
            ToolTokenCount.SkillBodyKey.ShouldBe("skillBody");
        }

        [Fact]
        public void CalculateSkillMetadata_ReturnsTheExactShareOfTheTotal()
        {
            var both = ToolTokenCount.CalculateSkillMetadata(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, SkillBodyText, ProbeInputSchema(), null);
            var descriptionOnly = ToolTokenCount.CalculateSkillMetadata(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, null, ProbeInputSchema(), null);
            var bodyOnly = ToolTokenCount.CalculateSkillMetadata(
                ProbeName, ProbeTitle, ProbeDescription, null, SkillBodyText, ProbeInputSchema(), null);
            var none = ToolTokenCount.CalculateSkillMetadata(
                ProbeName, ProbeTitle, ProbeDescription, null, null, ProbeInputSchema(), null);

            both.ShouldBe(SkillMetadataTokens);
            descriptionOnly.ShouldBe(ProbeTokensWithSkillDescriptionOnly - ProbeTokensWithoutSkillMetadata);
            bodyOnly.ShouldBe(ProbeTokensWithSkillBodyOnly - ProbeTokensWithoutSkillMetadata);

            // Pins the CONTRACT ("no skill metadata ⇒ no share"), not the early return that implements it:
            // that return is a fast path only, because Calculate's own IsNullOrEmpty gates already make the
            // two counts identical for this input. Deleting it leaves this line green, by construction.
            none.ShouldBe(0);
        }

        // ── ProxyTool (runtime tools) ───────────────────────────────────────────────────────────────

        [Fact]
        public void ProxyTool_TokenCount_MeasuresSkillMetadata_AndCachesTheSkillInclusiveValue()
        {
            var withMetadata = new ProxyTool(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, SkillBodyText,
                ProbeInputSchema(), null, null, null, null, null, NoopHandler());

            var withoutMetadata = new ProxyTool(
                ProbeName, ProbeTitle, ProbeDescription, null, null,
                ProbeInputSchema(), null, null, null, null, null, NoopHandler());

            // The two proxies differ in NOTHING but their skill metadata, so this delta is that metadata.
            withMetadata.TokenCount.ShouldBe(ProbeTokensWithBothMembers);
            withoutMetadata.TokenCount.ShouldBe(ProbeTokensWithoutSkillMetadata);
            withMetadata.SkillMetadataTokenCount.ShouldBe(SkillMetadataTokens);
            withoutMetadata.SkillMetadataTokenCount.ShouldBe(0);

            // The cached values are the skill-INCLUSIVE ones, so a correct Calculate cannot be undone by a
            // count that was frozen before the metadata was consulted.
            ReadCachedField(withMetadata, TotalCacheField).ShouldBe(ProbeTokensWithBothMembers);
            ReadCachedField(withMetadata, ShareCacheField).ShouldBe(SkillMetadataTokens);

            ShouldServeBothCountsFromTheCacheFields(withMetadata);
        }

        // ── RunTool (reflected tools) ───────────────────────────────────────────────────────────────

        [Fact]
        public void RunTool_TokenCount_MeasuresSkillMetadata_ByExactlyTheMeasuredDelta()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withMetadata = GetTool(toolManager, WithSkillToolName);
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            // Same signature, same [Description], same-length name and title: the ONLY difference between
            // the two entries is the skill metadata, so the whole delta is attributable to it.
            withMetadata.SkillDescription.ShouldBe(SkillDescriptionText);
            withMetadata.SkillBody.ShouldBe(SkillBodyText);
            withoutMetadata.SkillDescription.ShouldBeNull();
            withoutMetadata.SkillBody.ShouldBeNull();

            withMetadata.TokenCount.ShouldBe(withoutMetadata.TokenCount + SkillMetadataTokens);
            withMetadata.SkillMetadataTokenCount.ShouldBe(SkillMetadataTokens);
            withoutMetadata.SkillMetadataTokenCount.ShouldBe(0);
        }

        [Fact]
        public void RunTool_CachesTheSkillInclusiveCounts()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withMetadata = GetTool(toolManager, WithSkillToolName);
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            // Prime both caches; the assertions below read the fields, which are null until first access.
            _ = withMetadata.TokenCount;
            _ = withMetadata.SkillMetadataTokenCount;

            // Both cached values carry the skill metadata: the total is exactly the sibling's total plus the
            // measured payload, and the share is exactly that payload. Neither expectation is read back out
            // of the property under test — a share pinned against its own first read stays green under
            // "the share is always 0".
            ReadCachedField(withMetadata, TotalCacheField)
                .ShouldBe(withoutMetadata.TokenCount + SkillMetadataTokens, "the cached TOTAL must be skill-inclusive");
            ReadCachedField(withMetadata, ShareCacheField)
                .ShouldBe(SkillMetadataTokens, "the cached SHARE must be the measured payload");

            ShouldServeBothCountsFromTheCacheFields(withMetadata);
        }

        // ── Catalogue-level breakdown ───────────────────────────────────────────────────────────────

        [Fact]
        public void EnabledToolsSkillMetadataTokenCount_IsTheShareOfTheEnabledCatalogue()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withMetadata = GetTool(toolManager, WithSkillToolName);
            var withSecond = GetTool(toolManager, SecondSkillToolName);
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            toolManager.EnabledToolsTokenCount
                .ShouldBe(withMetadata.TokenCount + withSecond.TokenCount + withoutMetadata.TokenCount);

            // TWO skill-bearing tools with DIFFERENT-sized payloads, so this pins an addition: over this
            // catalogue a sum reads both shares while max / first / last each read only one of them.
            toolManager.EnabledToolsSkillMetadataTokenCount
                .ShouldBe(SkillMetadataTokens + SecondSkillMetadataTokens);
        }

        [Fact]
        public void EnabledToolsSkillMetadataTokenCount_ExcludesDisabledTools()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withSecond = GetTool(toolManager, SecondSkillToolName);
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            toolManager.SetToolEnabled(WithSkillToolName, false);

            // The disabled tool's share drops out and the second tool's survives. A NON-zero expectation is
            // what makes this discriminate: against zero, a filter that excluded everything would also pass.
            toolManager.EnabledToolsSkillMetadataTokenCount.ShouldBe(SecondSkillMetadataTokens);
            toolManager.EnabledToolsTokenCount.ShouldBe(withSecond.TokenCount + withoutMetadata.TokenCount);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private const string WithSkillToolName = "withSkillMetaTool";
        private const string SecondSkillToolName = "secondSkillMetaTool";
        private const string SansSkillToolName = "sansSkillMetaTool";
        private const string WithSkillToolTitle = "With Skill Meta";
        private const string SecondSkillToolTitle = "Second Skill Meta";
        private const string SansSkillToolTitle = "Sans Skill Meta";

        private IToolManager BuildSkillMetadataToolManager()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version);

            var withSkill = typeof(SkillMetadataToolClass).GetMethod(nameof(SkillMetadataToolClass.WithSkillMetadata));
            var secondSkill = typeof(SkillMetadataToolClass).GetMethod(nameof(SkillMetadataToolClass.WithSecondSkillMetadata));
            var sansSkill = typeof(SkillMetadataToolClass).GetMethod(nameof(SkillMetadataToolClass.SansSkillMetadata));

            builder.WithTool(WithSkillToolName, WithSkillToolTitle, typeof(SkillMetadataToolClass), withSkill!);
            builder.WithTool(SecondSkillToolName, SecondSkillToolTitle, typeof(SkillMetadataToolClass), secondSkill!);
            builder.WithTool(SansSkillToolName, SansSkillToolTitle, typeof(SkillMetadataToolClass), sansSkill!);

            return builder.Build(reflector).McpManager.ToolManager!;
        }

        private static IRunTool GetTool(IToolManager toolManager, string name)
        {
            var tool = toolManager.GetAllTools().FirstOrDefault(t => t.Name == name);
            tool.ShouldNotBeNull();
            return tool!;
        }

        private static FieldInfo CachedField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.ShouldNotBeNull();
            return field!;
        }

        private static int ReadCachedField(object instance, string fieldName)
        {
            var value = (int?)CachedField(instance, fieldName).GetValue(instance);
            value.ShouldNotBeNull();
            return value!.Value;
        }

        private static void WriteCachedField(object instance, string fieldName, int value)
            => CachedField(instance, fieldName).SetValue(instance, (int?)value);

        /// <summary>
        /// Proves both counts are SERVED FROM their cache fields rather than recomputed on every read.
        /// Reading a property twice and comparing cannot show that — a getter that recomputes agrees with
        /// itself — so the fields are overwritten with a value no Calculate over these fixtures can produce.
        /// </summary>
        private static void ShouldServeBothCountsFromTheCacheFields(IRunTool tool)
        {
            WriteCachedField(tool, TotalCacheField, CacheSentinel);
            WriteCachedField(tool, ShareCacheField, CacheSentinel);

            tool.TokenCount.ShouldBe(CacheSentinel, $"TokenCount must be served from {TotalCacheField}");
            tool.SkillMetadataTokenCount.ShouldBe(CacheSentinel, $"SkillMetadataTokenCount must be served from {ShareCacheField}");
        }

        private class SkillMetadataToolClass
        {
            [Description("Identical description on all fixtures so only the skill metadata differs.")]
            [AiSkillDescription(SkillDescriptionText)]
            [AiSkillBody(SkillBodyText)]
            public static string WithSkillMetadata(string value) => value;

            [Description("Identical description on all fixtures so only the skill metadata differs.")]
            [AiSkillDescription(SecondSkillDescriptionText)]
            public static string WithSecondSkillMetadata(string value) => value;

            [Description("Identical description on all fixtures so only the skill metadata differs.")]
            public static string SansSkillMetadata(string value) => value;
        }
    }
}
