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
using System.IO;
using System.Linq;
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Skills
{
    /// <summary>
    /// Per-kind SKILL.md metadata: <see cref="ConnectionConfig.ToolSkillMetadata"/> and
    /// <see cref="ConnectionConfig.StandaloneSkillMetadata"/> overlay the shared
    /// <see cref="ConnectionConfig.SkillMetadata"/> for tool pages and standalone skills respectively.
    /// </summary>
    [Collection("McpPlugin")]
    public class PerKindSkillMetadataTests : IDisposable
    {
        static readonly string[] ToolPages =
        {
            "skill-tool-both", "skill-tool-description-only", "skill-tool-body-only", "skill-tool-none",
            "standard-tool-a", "standard-tool-b", "standard-default"
        };
        static readonly string[] SystemToolPages = { "system-tool-x", "system-tool-y" };
        static readonly string[] StandaloneSkills = { "deploy-guide", "troubleshoot", "platform-info" };

        private readonly string _root = Path.Combine(Path.GetTempPath(), "PerKindSkillMetadata_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        /// <summary>Builds a plugin with standard tools, system tools and standalone skills; returns the metadata block of every SKILL.md by folder name.</summary>
        private Dictionary<string, string> Generate(string subdir, Action<ConnectionConfig> configure, bool customGenerator = false)
        {
            var path = Path.Combine(_root, subdir);
            var builder = new McpPluginBuilder(new Version());
            builder.WithConfig(c =>
            {
                c.SkillsPath = path;
                c.KeepConnected = false;
                configure(c);
            });
            if (customGenerator) builder.WithSkillFileGenerator<EngineGenerator>();
            builder.WithTools(typeof(SkillAnnotatedToolClass), typeof(MixedToolTypeClass))
                .WithSkills(typeof(AnnotatedSkillClass));

            using var plugin = (McpPlugin)builder.Build(new Reflector());

            return Directory.GetFiles(path, "SKILL.md", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetFileName(Path.GetDirectoryName(file))!,
                file => MetadataBlock(File.ReadAllText(file)));
        }

        private static string MetadataBlock(string text)
        {
            var frontMatter = text.Replace("\r\n", "\n").Split(new[] { "---" }, StringSplitOptions.None)[1];
            return frontMatter.Substring(frontMatter.IndexOf("metadata:\n", StringComparison.Ordinal));
        }

        private static void ShouldCoverEveryKind(Dictionary<string, string> pages)
        {
            pages.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(
                ToolPages.Concat(SystemToolPages).Concat(StandaloneSkills).OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void SharedOnly_WritesTheSameMetadataToEveryKind_AsBefore()
        {
            var pages = Generate("shared", c =>
            {
                c.SkillMetadata["generated-by"] = "unity-mcp";
                c.SkillMetadata["ai-game-dev"] = "exclude";
            });

            ShouldCoverEveryKind(pages);
            foreach (var page in pages)
                page.Value.ShouldBe("metadata:\n  generated-by: unity-mcp\n  ai-game-dev: exclude\n", page.Key);
        }

        [Fact]
        public void SharedOnly_IsByteIdenticalToExplicitlyEmptyOverlays()
        {
            var sharedOnly = Generate("a", c => c.SkillMetadata["ai-game-dev"] = "exclude");
            var withEmptyOverlays = Generate("b", c =>
            {
                c.SkillMetadata["ai-game-dev"] = "exclude";
                c.ToolSkillMetadata = new Dictionary<string, string>();
                c.StandaloneSkillMetadata = new Dictionary<string, string>();
            });

            withEmptyOverlays.ShouldBe(sharedOnly);
        }

        [Fact]
        public void NoConfiguration_KeepsTheProvenanceDefaultOnEveryKind()
        {
            var pages = Generate("default", _ => { });

            ShouldCoverEveryKind(pages);
            foreach (var page in pages)
                page.Value.ShouldBe("metadata:\n  generated-by: mcp-plugin-dotnet\n", page.Key);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PerKindOverlays_SplitToolPagesFromStandaloneSkills_InOneGeneration(bool customGenerator)
        {
            var pages = Generate("overlays", c =>
            {
                c.SkillMetadata["generated-by"] = "unity-mcp";
                c.ToolSkillMetadata["ai-game-dev"] = "exclude";
                c.StandaloneSkillMetadata["ai-game-dev"] = "include";
            }, customGenerator);

            ShouldCoverEveryKind(pages);
            foreach (var tool in ToolPages.Concat(SystemToolPages))
                pages[tool].ShouldBe("metadata:\n  generated-by: unity-mcp\n  ai-game-dev: exclude\n", tool);
            foreach (var skill in StandaloneSkills)
                pages[skill].ShouldBe("metadata:\n  generated-by: unity-mcp\n  ai-game-dev: include\n", skill);
        }

        [Fact]
        public void Overlay_OverridesSharedKeys_IncludingGeneratedBy_AndLeavesTheOtherKindShared()
        {
            var pages = Generate("override", c =>
            {
                c.SkillMetadata["generated-by"] = "godot-mcp";
                c.SkillMetadata["ai-game-dev"] = "exclude";
                c.StandaloneSkillMetadata["ai-game-dev"] = "include";
                c.StandaloneSkillMetadata["generated-by"] = "godot-mcp-knowledge";
            });

            ShouldCoverEveryKind(pages);
            foreach (var tool in ToolPages.Concat(SystemToolPages))
                pages[tool].ShouldBe("metadata:\n  generated-by: godot-mcp\n  ai-game-dev: exclude\n", tool);
            foreach (var skill in StandaloneSkills)
                pages[skill].ShouldBe("metadata:\n  generated-by: godot-mcp-knowledge\n  ai-game-dev: include\n", skill);
        }

        [Fact]
        public void ToolOverlay_AppliesToSystemTools()
        {
            var pages = Generate("system", c => c.ToolSkillMetadata["kind"] = "tool");

            foreach (var tool in SystemToolPages)
                pages[tool].ShouldBe("metadata:\n  generated-by: mcp-plugin-dotnet\n  kind: tool\n", tool);
            foreach (var skill in StandaloneSkills)
                pages[skill].ShouldBe("metadata:\n  generated-by: mcp-plugin-dotnet\n", skill);
        }

        [Fact]
        public void MergeMetadata_OverlayWins_AndToleratesNulls()
        {
            SkillFileGenerator.MergeMetadata(null, null).ShouldBeEmpty();
            SkillFileGenerator.MergeMetadata(
                new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
                new Dictionary<string, string> { ["b"] = "3", ["c"] = "4" })
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .ShouldBe(new[]
                {
                    new KeyValuePair<string, string>("a", "1"),
                    new KeyValuePair<string, string>("b", "3"),
                    new KeyValuePair<string, string>("c", "4"),
                });
        }

        [Fact]
        public void EffectiveMetadata_ReflectsGeneratorProperties()
        {
            var generator = new SkillFileGenerator
            {
                Metadata = new Dictionary<string, string> { ["shared"] = "s", ["ai-game-dev"] = "exclude" },
                StandaloneSkillMetadata = new Dictionary<string, string> { ["ai-game-dev"] = "include" },
            };

            generator.EffectiveToolSkillMetadata["ai-game-dev"].ShouldBe("exclude");
            generator.EffectiveStandaloneSkillMetadata["ai-game-dev"].ShouldBe("include");
            generator.EffectiveStandaloneSkillMetadata["shared"].ShouldBe("s");
        }

        public sealed class EngineGenerator : SkillFileGenerator { }
    }
}
