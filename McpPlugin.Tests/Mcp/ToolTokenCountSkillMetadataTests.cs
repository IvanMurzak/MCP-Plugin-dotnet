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

            nulls.ShouldBe(ProbeTokensWithoutSkillMetadata);
            empties.ShouldBe(ProbeTokensWithoutSkillMetadata);
        }

        [Fact]
        public void Calculate_WithSkillMetadata_AddsExactlyTheMeasuredPayload()
        {
            var withMetadata = ToolTokenCount.Calculate(
                ProbeName, ProbeTitle, ProbeDescription, SkillDescriptionText, SkillBodyText, ProbeInputSchema(), null);

            withMetadata.ShouldBe(ProbeTokensWithBothMembers);
            (withMetadata - ProbeTokensWithoutSkillMetadata).ShouldBe(SkillMetadataTokens);
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

            // Repeated reads are served from the cache — and the cached value is the skill-inclusive one,
            // so a correct Calculate cannot be undone by a count that was frozen before it was consulted.
            withMetadata.TokenCount.ShouldBe(ProbeTokensWithBothMembers);
            withMetadata.SkillMetadataTokenCount.ShouldBe(SkillMetadataTokens);

            ReadCachedField(withMetadata, "_cachedTokenCount").ShouldBe(ProbeTokensWithBothMembers);
            ReadCachedField(withMetadata, "_cachedSkillMetadataTokenCount").ShouldBe(SkillMetadataTokens);
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

            var firstTotal = withMetadata.TokenCount;
            var firstShare = withMetadata.SkillMetadataTokenCount;

            withMetadata.TokenCount.ShouldBe(firstTotal);
            withMetadata.SkillMetadataTokenCount.ShouldBe(firstShare);

            ReadCachedField(withMetadata, "_cachedTokenCount").ShouldBe(firstTotal);
            ReadCachedField(withMetadata, "_cachedSkillMetadataTokenCount").ShouldBe(firstShare);

            // The cached value is the one that carries the skill metadata: it is exactly the sibling's
            // cached total plus the measured payload, so a count frozen before the metadata was consulted
            // could not produce it.
            ReadCachedField(withMetadata, "_cachedTokenCount")
                .ShouldBe(withoutMetadata.TokenCount + SkillMetadataTokens);
        }

        // ── Catalogue-level breakdown ───────────────────────────────────────────────────────────────

        [Fact]
        public void EnabledToolsSkillMetadataTokenCount_IsTheShareOfTheEnabledCatalogue()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withMetadata = GetTool(toolManager, WithSkillToolName);
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            toolManager.EnabledToolsTokenCount.ShouldBe(withMetadata.TokenCount + withoutMetadata.TokenCount);
            toolManager.EnabledToolsSkillMetadataTokenCount.ShouldBe(SkillMetadataTokens);
        }

        [Fact]
        public void EnabledToolsSkillMetadataTokenCount_ExcludesDisabledTools()
        {
            var toolManager = BuildSkillMetadataToolManager();
            var withoutMetadata = GetTool(toolManager, SansSkillToolName);

            toolManager.SetToolEnabled(WithSkillToolName, false);

            toolManager.EnabledToolsSkillMetadataTokenCount.ShouldBe(0);
            toolManager.EnabledToolsTokenCount.ShouldBe(withoutMetadata.TokenCount);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private const string WithSkillToolName = "withSkillMetaTool";
        private const string SansSkillToolName = "sansSkillMetaTool";
        private const string WithSkillToolTitle = "With Skill Meta";
        private const string SansSkillToolTitle = "Sans Skill Meta";

        private IToolManager BuildSkillMetadataToolManager()
        {
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version);

            var withSkill = typeof(SkillMetadataToolClass).GetMethod(nameof(SkillMetadataToolClass.WithSkillMetadata));
            var sansSkill = typeof(SkillMetadataToolClass).GetMethod(nameof(SkillMetadataToolClass.SansSkillMetadata));

            builder.WithTool(WithSkillToolName, WithSkillToolTitle, typeof(SkillMetadataToolClass), withSkill!);
            builder.WithTool(SansSkillToolName, SansSkillToolTitle, typeof(SkillMetadataToolClass), sansSkill!);

            return builder.Build(reflector).McpManager.ToolManager!;
        }

        private static IRunTool GetTool(IToolManager toolManager, string name)
        {
            var tool = toolManager.GetAllTools().FirstOrDefault(t => t.Name == name);
            tool.ShouldNotBeNull();
            return tool!;
        }

        private static int ReadCachedField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.ShouldNotBeNull();
            var value = (int?)field!.GetValue(instance);
            value.ShouldNotBeNull();
            return value!.Value;
        }

        private class SkillMetadataToolClass
        {
            [Description("Identical description on both fixtures so only the skill metadata differs.")]
            [AiSkillDescription(SkillDescriptionText)]
            [AiSkillBody(SkillBodyText)]
            public static string WithSkillMetadata(string value) => value;

            [Description("Identical description on both fixtures so only the skill metadata differs.")]
            public static string SansSkillMetadata(string value) => value;
        }
    }
}
