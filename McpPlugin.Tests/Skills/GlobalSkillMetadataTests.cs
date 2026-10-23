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
using System.Text;
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Skills
{
    [Collection("McpPlugin")]
    public class GlobalSkillMetadataTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "GlobalSkillMetadata_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Theory]
        [InlineData("default", false)]
        [InlineData("generic", false)]
        [InlineData("instance", false)]
        [InlineData("default", true)]
        [InlineData("generic", true)]
        [InlineData("instance", true)]
        public void Build_AppliesGlobalMetadataBeforeInitialGeneration(string generatorKind, bool externalConfig)
        {
            var config = new ConnectionConfig { SkillsPath = _root, KeepConnected = false };
            var builder = new McpPluginBuilder(new Version());
            if (externalConfig) builder.SetConfig(config);
            builder.WithConfig(c =>
            {
                c.SkillsPath = _root;
                c.KeepConnected = false;
                c.SkillMetadata["generated-by"] = "unity-mcp";
                c.SkillMetadata["ai-game-dev"] = "exclude";
            });
            if (generatorKind == "generic") builder.WithSkillFileGenerator<EngineGenerator>();
            if (generatorKind == "instance") builder.WithSkillFileGenerator(new EngineGenerator());
            builder.WithTools(typeof(SkillAnnotatedToolClass)).WithSkills(typeof(AnnotatedSkillClass));

            using var plugin = (McpPlugin)builder.Build(new Reflector());

            var paths = Directory.GetFiles(_root, "SKILL.md", SearchOption.AllDirectories);
            paths.Length.ShouldBe(7); // Four tool pages and three enabled standalone skills.
            foreach (var path in paths)
            {
                var text = File.ReadAllText(path).Replace("\r\n", "\n");
                var frontMatter = text.Split(new[] { "---" }, StringSplitOptions.None)[1];
                frontMatter.ShouldContain("metadata:\n  generated-by: unity-mcp\n  ai-game-dev: exclude\n");
                frontMatter.ShouldNotContain("mcp-plugin-dotnet");
            }
        }

        [Fact]
        public void AppendMetadata_IsIndependentOfInsertionOrder_AndDoesNotDuplicateGeneratedBy()
        {
            var first = new StringBuilder();
            var second = new StringBuilder();
            SkillFileGenerator.AppendMetadata(first, new Dictionary<string, string>
            {
                ["z-last"] = "last", ["generated-by"] = "godot-mcp", ["ai-game-dev"] = "exclude"
            });
            SkillFileGenerator.AppendMetadata(second, new Dictionary<string, string>
            {
                ["ai-game-dev"] = "exclude", ["generated-by"] = "godot-mcp", ["z-last"] = "last"
            });

            first.ToString().ShouldBe(second.ToString());
            first.ToString().Replace("\r\n", "\n").ShouldBe(
                "metadata:\n  generated-by: godot-mcp\n  ai-game-dev: exclude\n  z-last: last\n");
        }

        [Theory]
        [InlineData("true", "\"true\"")]
        [InlineData("null", "\"null\"")]
        [InlineData("123", "\"123\"")]
        [InlineData("", "\"\"")]
        [InlineData("line\n---\nname: injected", "\"line\\n---\\nname: injected\"")]
        public void AppendMetadata_PreservesStringValuesAndCannotInjectFrontMatter(string value, string expected)
        {
            var sb = new StringBuilder();
            SkillFileGenerator.AppendMetadata(sb, new Dictionary<string, string> { ["custom"] = value });
            sb.ToString().Replace("\r\n", "\n").ShouldBe(
                "metadata:\n  generated-by: mcp-plugin-dotnet\n  custom: " + expected + "\n");
        }

        public sealed class EngineGenerator : SkillFileGenerator { }
    }
}
