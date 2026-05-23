/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

// This fixture intentionally uses BOTH the new canonical [Ai*] attributes AND the legacy
// [Obsolete] [McpPlugin*] aliases on different members within the same class. It exists to
// prove that reflection lookups for the new canonical attribute types (AiToolAttribute,
// AiPromptAttribute, AiResourceAttribute, AiSkillAttribute) discover both old-style and
// new-style decorations correctly via inheritance — which is what makes the deprecation path
// backward-compatible for existing consumers shipped against earlier package versions.
//
// The CS0618 suppression is scoped to this file only — the build is otherwise expected to
// emit zero [Obsolete] warnings on in-repo code.

#pragma warning disable CS0618 // intentional use of obsolete McpPlugin* aliases to exercise discovery

using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin.Tests.Data.Annotations
{
    [AiToolType] // class container uses the new canonical name
    public static class MixedAliasToolClass
    {
        [AiTool("alias-tool-new", "Decorated with new [AiTool]")]
        public static string NewStyle() => "new";

        [McpPluginTool("alias-tool-old", "Decorated with legacy [McpPluginTool] obsolete alias")]
        public static string OldStyle() => "old";
    }

    [AiPromptType]
    public static class MixedAliasPromptClass
    {
        [AiPrompt(Name = "alias-prompt-new")]
        public static string NewStyle() => "new";

        [McpPluginPrompt(Name = "alias-prompt-old")]
        public static string OldStyle() => "old";
    }

    [AiResourceType]
    public static class MixedAliasResourceClass
    {
        [AiResource(Route = "test://alias-resource-new/{id}", Name = "alias-resource-new", ListResources = nameof(ListNew))]
        public static ResponseResourceContent[] GetNew(string id)
            => new[] { ResponseResourceContent.CreateText($"test://alias-resource-new/{id}", "new") };

        public static ResponseListResource[] ListNew()
            => new[] { new ResponseListResource("test://alias-resource-new/1", "alias-resource-new") };

        [McpPluginResource(Route = "test://alias-resource-old/{id}", Name = "alias-resource-old", ListResources = nameof(ListOld))]
        public static ResponseResourceContent[] GetOld(string id)
            => new[] { ResponseResourceContent.CreateText($"test://alias-resource-old/{id}", "old") };

        public static ResponseListResource[] ListOld()
            => new[] { new ResponseListResource("test://alias-resource-old/1", "alias-resource-old") };
    }

    [AiSkillType]
    public static class MixedAliasSkillClass
    {
        [AiSkill("alias-skill-new", "Decorated with new [AiSkill]")]
        public const string NewStyle = "# New\n\nDecorated with the new canonical attribute.";

        [McpPluginSkill("alias-skill-old", "Decorated with legacy [McpPluginSkill] obsolete alias")]
        public const string OldStyle = "# Old\n\nDecorated with the deprecated alias.";
    }
}

#pragma warning restore CS0618
