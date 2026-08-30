/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Linq;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// <c>tools/list</c> surfaces a tool's skill metadata as the <c>_meta</c> keys
    /// <c>skillDescription</c> / <c>skillBody</c>. The projection under test is
    /// <c>ExtensionsTool.ToTool()</c> — the exact expression
    /// <c>ToolRouter.ListAll</c> maps every plugin tool through.
    /// The composition is deliberately tools-only — see the <c>remarks</c> on
    /// <c>ExtensionsListMeta.BuildToolMeta</c> for why the shared helper stays untouched.
    ///
    /// <para>Every case here runs INSIDE <see cref="SkillMetaClientScope"/>, i.e. as a caller that
    /// sent <c>X-McpPlugin-Skill-Meta: 1</c>. That is load-bearing rather than incidental: the keys
    /// are now gated, so without the opt-in the "no key is emitted" cases below would hold for the
    /// gate's reason instead of their own and could no longer tell the composition apart from a
    /// server that never learned to emit skill metadata at all. What the gate itself does to a
    /// caller that did NOT opt in is pinned separately, in <c>SkillMetaClientGateTests</c>.</para>
    /// </summary>
    public class ToolListSkillMetaTests
    {
        const string SkillDescriptionText = "Creates a primitive object in the scene";
        const string SkillBodyText = "# GameObject create\n\nUse `path` to nest the new object.";

        static ResponseListTool Tool(
            bool enabled = true,
            string? skillDescription = null,
            string? skillBody = null)
            => new ResponseListTool
            {
                Name = "gameobject-create",
                Enabled = enabled,
                InputSchema = Consts.MCP.EmptyInputSchema,
                SkillDescription = skillDescription,
                SkillBody = skillBody
            };

        [Fact]
        public void ToTool_EmitsBothSkillKeys_WhenBothPresent()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            var meta = Tool(skillDescription: SkillDescriptionText, skillBody: SkillBodyText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta[ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);

            // An ENABLED tool must not gain an `enabled` key. The exact key SET says that in
            // one assertion and pins emission ORDER too; the value assertions above are the
            // positive control proving the object was built at all.
            meta.Select(kv => kv.Key).ShouldBe(
                new[] { ExtensionsListMeta.SkillDescriptionKey, ExtensionsListMeta.SkillBodyKey });
        }

        [Fact]
        public void ToTool_EmitsOnlySkillDescription_WhenBodyAbsent()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            var meta = Tool(skillDescription: SkillDescriptionText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillDescriptionKey });
        }

        [Fact]
        public void ToTool_EmitsOnlySkillBody_WhenDescriptionAbsent()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            var meta = Tool(skillBody: SkillBodyText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillBodyKey });
        }

        [Fact]
        public void ToTool_CombinesEnabledFalseWithSkillKeys_WhenDisabledAndAttributed()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            var meta = Tool(enabled: false, skillDescription: SkillDescriptionText, skillBody: SkillBodyText)
                .ToTool()
                .Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
            meta[ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta[ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);
            // Same key-SET form as the two-key case above: this is the only test exercising
            // all three keys, so it is the only one that can pin their emission order.
            meta.Select(kv => kv.Key).ShouldBe(new[]
            {
                ExtensionsListMeta.EnabledKey,
                ExtensionsListMeta.SkillDescriptionKey,
                ExtensionsListMeta.SkillBodyKey
            });
        }

        [Fact]
        public void ToTool_OmitsMetaEntirely_WhenEnabledAndNoSkillMetadata()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            // The unchanged default wire shape: this is what every third-party client sees
            // today, and the whole point of composing instead of always emitting an object.
            Tool().ToTool().Meta.ShouldBeNull();
        }

        [Fact]
        public void ToTool_EmitsOnlyEnabledKey_WhenDisabledAndNoSkillMetadata()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            var meta = Tool(enabled: false).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.EnabledKey });
        }

        [Fact]
        public void ToTool_TreatsBlankSkillMetadataAsAbsent()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            // A tool whose attribute carried an empty string must not push an empty key onto
            // the wire — clients render the catalog line from it.
            Tool(skillDescription: string.Empty, skillBody: string.Empty).ToTool().Meta.ShouldBeNull();
        }

        [Fact]
        public void ToTool_RelaysWhitespaceOnlySkillBody_BecauseTheGuardIsIsNullOrEmpty()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            // BuildToolMeta guards with IsNullOrEmpty, NOT IsNullOrWhiteSpace, so a
            // whitespace-only value is relayed verbatim; SkillFileGenerator drops it from
            // SKILL.md. That divergence is deliberate and was prose-only until this test:
            // swapping the guard to IsNullOrWhiteSpace reddened NOTHING and would have
            // silently turned the comment at the guard into a false claim.
            var meta = Tool(skillBody: "   ").ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe("   ");
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillBodyKey });
        }

        [Fact]
        public void BuildToolMeta_EmitsExactlyTheSkillKeys_WhenCalledDirectly()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            // Pins the PUBLIC helper directly. ToTool() assigns BuildToolMeta(response)
            // verbatim today, so this deliberately overlaps
            // ToTool_EmitsOnlySkillDescription_WhenBodyAbsent above; it earns its place only
            // because BuildToolMeta ships as public API of com.IvanMurzak.McpPlugin.Server and
            // a second call site could use it without going through ToTool().
            var meta = ExtensionsListMeta.BuildToolMeta(Tool(skillDescription: SkillDescriptionText));

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillDescriptionKey });
        }

        [Fact]
        public void BuildEnabledMeta_StillEmitsExactlyTheEnabledKey()
        {
            using var optIn = SkillMetaClientScope.OptIn();

            // The three non-tool call sites (prompts / resources / resource templates) assign
            // BuildEnabledMeta's result WHOLESALE, so teaching it the skill keys would leak
            // them onto primitives that have no such metadata. This reddens if the skill keys
            // are folded into the shared helper instead of composed on top of it.
            var meta = ExtensionsListMeta.BuildEnabledMeta(enabled: false);

            meta.ShouldNotBeNull();
            meta!.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.EnabledKey });
        }

        [Fact]
        public void SkillMetaKeys_AreTheLiteralWireNames()
        {
            // Every other assertion in this file reads the keys THROUGH these constants, so
            // changing a constant's VALUE would rename the key for every MCP client while
            // reddening nothing. These two strings ARE the wire contract; pin them literally.
            ExtensionsListMeta.SkillDescriptionKey.ShouldBe("skillDescription");
            ExtensionsListMeta.SkillBodyKey.ShouldBe("skillBody");
        }
    }
}
