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
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// <c>tools/list</c> surfaces a tool's skill metadata as the <c>_meta</c> keys
    /// <c>skillDescription</c> / <c>skillBody</c>. The projection under test is
    /// <c>ExtensionsTool.ToTool()</c> — the exact expression
    /// <c>ToolRouter.ListAll</c> maps every plugin tool through.
    /// <para>
    /// The composition is deliberately tools-only:
    /// <c>ExtensionsListMeta.BuildEnabledMeta</c> keeps its null-when-enabled contract
    /// because prompts, resources and resource templates assign its result wholesale.
    /// </para>
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
            var meta = Tool(skillDescription: SkillDescriptionText, skillBody: SkillBodyText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta[ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);

            // An ENABLED tool must not gain an `enabled` key: the two assertions above are
            // the positive control proving the object was built, so this absence is a
            // statement about `enabled` alone.
            meta.ContainsKey(ExtensionsListMeta.EnabledKey).ShouldBeFalse();
            meta.Count.ShouldBe(2);
        }

        [Fact]
        public void ToTool_EmitsOnlySkillDescription_WhenBodyAbsent()
        {
            var meta = Tool(skillDescription: SkillDescriptionText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillDescriptionKey });
        }

        [Fact]
        public void ToTool_EmitsOnlySkillBody_WhenDescriptionAbsent()
        {
            var meta = Tool(skillBody: SkillBodyText).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.SkillBodyKey });
        }

        [Fact]
        public void ToTool_CombinesEnabledFalseWithSkillKeys_WhenDisabledAndAttributed()
        {
            var meta = Tool(enabled: false, skillDescription: SkillDescriptionText, skillBody: SkillBodyText)
                .ToTool()
                .Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
            meta[ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
            meta[ExtensionsListMeta.SkillBodyKey]!.GetValue<string>().ShouldBe(SkillBodyText);
            meta.Count.ShouldBe(3);
        }

        [Fact]
        public void ToTool_OmitsMetaEntirely_WhenEnabledAndNoSkillMetadata()
        {
            // The unchanged default wire shape: this is what every third-party client sees
            // today, and the whole point of composing instead of always emitting an object.
            Tool().ToTool().Meta.ShouldBeNull();
        }

        [Fact]
        public void ToTool_EmitsOnlyEnabledKey_WhenDisabledAndNoSkillMetadata()
        {
            var meta = Tool(enabled: false).ToTool().Meta;

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.EnabledKey]!.GetValue<bool>().ShouldBeFalse();
            meta.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.EnabledKey });
        }

        [Fact]
        public void ToTool_TreatsBlankSkillMetadataAsAbsent()
        {
            // A tool whose attribute carried an empty string must not push an empty key onto
            // the wire — clients render the catalog line from it.
            Tool(skillDescription: string.Empty, skillBody: string.Empty).ToTool().Meta.ShouldBeNull();
        }

        [Fact]
        public void BuildToolMeta_MatchesToTool_ForTheAttributedCase()
        {
            // Pins the helper itself, so a future call site gets the same composition.
            var meta = ExtensionsListMeta.BuildToolMeta(Tool(skillDescription: SkillDescriptionText));

            meta.ShouldNotBeNull();
            meta![ExtensionsListMeta.SkillDescriptionKey]!.GetValue<string>().ShouldBe(SkillDescriptionText);
        }

        [Fact]
        public void BuildEnabledMeta_StillEmitsExactlyTheEnabledKey()
        {
            // The three non-tool call sites (prompts / resources / resource templates) assign
            // BuildEnabledMeta's result WHOLESALE, so teaching it the skill keys would leak
            // them onto primitives that have no such metadata. This reddens if the skill keys
            // are folded into the shared helper instead of composed on top of it.
            var meta = ExtensionsListMeta.BuildEnabledMeta(enabled: false);

            meta.ShouldNotBeNull();
            meta!.Select(kv => kv.Key).ShouldBe(new[] { ExtensionsListMeta.EnabledKey });
        }
    }
}
