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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Skills;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Skills
{
    /// <summary>
    /// Pins the provenance marker stamped into every generated SKILL.md front-matter, in BOTH
    /// emission paths (tool skills and custom skill-content skills), plus the two properties a
    /// consumer relies on when it uses the marker to de-duplicate generated skills: regeneration
    /// is byte-stable, and a skill file the generator does not own is never rewritten.
    /// </summary>
    public class SkillFileGeneratorProvenanceTests : IDisposable
    {
        // Literal expectations, deliberately NOT built from SkillFileGenerator's own constants:
        // this is the on-disk contract a front-matter reader parses, so a changed constant must
        // redden these tests rather than move with them.
        const string MarkerBlockLine = "metadata:";
        const string MarkerEntryLine = "  generated-by: mcp-plugin-dotnet";

        const string Host = "http://localhost:8080";

        readonly string _tempDir;

        public SkillFileGeneratorProvenanceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"SkillFileGeneratorProvenanceTests_{Guid.NewGuid():N}");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Marker present, in the front matter, in BOTH emission paths ──────────

        [Fact]
        public void Generate_Tool_StampsMarkerInsideFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "marked-tool", Description = "A tool." };

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "marked-tool", "SKILL.md"));
            var frontMatter = FrontMatter(lines);

            frontMatter.ShouldContain(MarkerBlockLine);
            frontMatter.ShouldContain(MarkerEntryLine);
            // Order matters for YAML: the nested entry must directly follow its block key.
            frontMatter.IndexOf(MarkerEntryLine).ShouldBe(frontMatter.IndexOf(MarkerBlockLine) + 1);
        }

        [Fact]
        public void Generate_SkillContent_StampsMarkerInsideFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var skill = new SkillContent("marked-skill", "A skill.", "# Marked\n\nBody.\n");

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "marked-skill", "SKILL.md"));
            var frontMatter = FrontMatter(lines);

            frontMatter.ShouldContain(MarkerBlockLine);
            frontMatter.ShouldContain(MarkerEntryLine);
            frontMatter.IndexOf(MarkerEntryLine).ShouldBe(frontMatter.IndexOf(MarkerBlockLine) + 1);
        }

        [Fact]
        public void Generate_Tool_StampsMarkerExactlyOnce()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "once-tool" };

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "once-tool", "SKILL.md"));
            CountOf(lines, MarkerEntryLine).ShouldBe(1);
        }

        [Fact]
        public void Generate_SkillContent_StampsMarkerExactlyOnce()
        {
            var generator = new SkillFileGenerator();
            var skill = new SkillContent("once-skill", "A skill.", "Body");

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "once-skill", "SKILL.md"));
            CountOf(lines, MarkerEntryLine).ShouldBe(1);
        }

        /// <summary>
        /// A multi-line description is emitted as a YAML literal block scalar (<c>|-</c>) whose body
        /// lines are indented by two spaces — the same indent the marker's nested entry uses. The
        /// marker must therefore stay a TOP-LEVEL front-matter key: its block line at column 0 is
        /// what terminates the block scalar. Without that, a reader parses the marker as description
        /// text and the skill reads as user-authored.
        /// </summary>
        [Fact]
        public void Generate_Tool_MarkerIsTopLevelEvenAfterABlockScalarDescription()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool
            {
                Name = "multiline-tool",
                Description = "First line of the description.\nSecond line.\nThird line."
            };

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "multiline-tool", "SKILL.md"));
            var frontMatter = FrontMatter(lines);

            // Positive control: the fixture really did produce a block scalar, so the block-scalar
            // hazard this test exists for is actually present.
            frontMatter.ShouldContain("description: |-");
            frontMatter.ShouldContain("  Second line.");

            // The marker's block key sits at column 0 (closing the block scalar) and its entry
            // directly follows.
            frontMatter.ShouldContain(MarkerBlockLine);
            frontMatter.IndexOf(MarkerEntryLine).ShouldBe(frontMatter.IndexOf(MarkerBlockLine) + 1);
        }

        // ── Idempotence ──────────────────────────────────────────────────────────

        [Fact]
        public void Generate_Tool_RegeneratingOverAnExistingDirectoryIsByteStable()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "stable-tool", Description = "A tool." };
            var filePath = Path.Combine(_tempDir, "stable-tool", "SKILL.md");

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();
            var first = File.ReadAllBytes(filePath);

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();
            var second = File.ReadAllBytes(filePath);

            second.ShouldBe(first);
            // Positive control: the bytes that stayed equal are bytes that carry the marker — an
            // equality over two empty/marker-free files would prove nothing about this task.
            CountOf(ReadLines(filePath), MarkerEntryLine).ShouldBe(1);
        }

        [Fact]
        public void Generate_SkillContent_RegeneratingOverAnExistingDirectoryIsByteStable()
        {
            var generator = new SkillFileGenerator();
            var skill = new SkillContent("stable-skill", "A skill.", "# Stable\n\nBody.\n");
            var filePath = Path.Combine(_tempDir, "stable-skill", "SKILL.md");

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();
            var first = File.ReadAllBytes(filePath);

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();
            var second = File.ReadAllBytes(filePath);

            second.ShouldBe(first);
            CountOf(ReadLines(filePath), MarkerEntryLine).ShouldBe(1);
        }

        // ── Foreign skill files are left alone ───────────────────────────────────

        [Fact]
        public void Generate_Tool_LeavesAHandCraftedSkillOfAnotherToolByteIntact()
        {
            const string handCrafted =
                "---\n" +
                "name: hand-written\n" +
                "description: Authored by a human, not by this generator.\n" +
                "---\n" +
                "\n" +
                "# Hand Written\n";

            var handCraftedPath = Path.Combine(_tempDir, "hand-written", "SKILL.md");
            Directory.CreateDirectory(Path.Combine(_tempDir, "hand-written"));
            File.WriteAllText(handCraftedPath, handCrafted);
            var before = File.ReadAllBytes(handCraftedPath);

            var generator = new SkillFileGenerator();
            generator.Generate(new[] { new MockRunTool { Name = "other-tool" } }, _tempDir, Host).ShouldBeTrue();

            // Positive control FIRST: the generation pass really did run and really did write a
            // marked file, so the untouched assertion below cannot pass by nothing happening.
            CountOf(ReadLines(Path.Combine(_tempDir, "other-tool", "SKILL.md")), MarkerEntryLine).ShouldBe(1);

            File.ReadAllBytes(handCraftedPath).ShouldBe(before);
            CountOf(ReadLines(handCraftedPath), MarkerEntryLine).ShouldBe(0);
            CountOf(ReadLines(handCraftedPath), MarkerBlockLine).ShouldBe(0);
        }

        [Fact]
        public void Generate_SkillContent_LeavesAHandCraftedSkillOfAnotherSkillByteIntact()
        {
            const string handCrafted =
                "---\n" +
                "name: hand-written-skill\n" +
                "description: Authored by a human, not by this generator.\n" +
                "---\n" +
                "\n" +
                "# Hand Written Skill\n";

            var handCraftedPath = Path.Combine(_tempDir, "hand-written-skill", "SKILL.md");
            Directory.CreateDirectory(Path.Combine(_tempDir, "hand-written-skill"));
            File.WriteAllText(handCraftedPath, handCrafted);
            var before = File.ReadAllBytes(handCraftedPath);

            var generator = new SkillFileGenerator();
            generator.Generate(new[] { new SkillContent("other-skill", "A skill.", "Body") }, _tempDir).ShouldBeTrue();

            CountOf(ReadLines(Path.Combine(_tempDir, "other-skill", "SKILL.md")), MarkerEntryLine).ShouldBe(1);

            File.ReadAllBytes(handCraftedPath).ShouldBe(before);
            CountOf(ReadLines(handCraftedPath), MarkerEntryLine).ShouldBe(0);
        }

        // ── The published constants describe the bytes actually written ──────────

        [Fact]
        public void ProvenanceConstants_MatchTheBytesWrittenToDisk()
        {
            SkillFileGenerator.ProvenanceBlockKey.ShouldBe("metadata");
            SkillFileGenerator.ProvenanceKey.ShouldBe("generated-by");
            SkillFileGenerator.ProvenanceValue.ShouldBe("mcp-plugin-dotnet");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        static string[] ReadLines(string path)
            => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

        /// <summary>Returns the lines strictly between the opening and closing <c>---</c>.</summary>
        static List<string> FrontMatter(string[] lines)
        {
            lines.Length.ShouldBeGreaterThan(0);
            lines[0].ShouldBe("---");

            var result = new List<string>();
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i] == "---")
                    return result;
                result.Add(lines[i]);
            }

            throw new InvalidOperationException("SKILL.md front-matter block was never closed by a '---' line.");
        }

        static int CountOf(string[] lines, string line)
        {
            var count = 0;
            foreach (var candidate in lines)
                if (candidate == line)
                    count++;
            return count;
        }

        class MockRunTool : IRunTool
        {
            public string Name { get; init; } = "mock-tool";
            public string? Title { get; init; } = "Mock Tool";
            public string? Description { get; init; } = "A mock tool for testing.";
            public string? SkillDescription { get; init; }
            public string? SkillBody { get; init; }
            public JsonNode? InputSchema { get; init; }
            public JsonNode? OutputSchema { get; init; }
            public bool Enabled { get; set; } = true;
            public bool? ReadOnlyHint => null;
            public bool? DestructiveHint => null;
            public bool? IdempotentHint => null;
            public bool? OpenWorldHint => null;
            public int TokenCount => 0;

            public Task<ResponseCallTool> Run(string requestId, IReadOnlyDictionary<string, JsonElement>? namedParameters, CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }
    }
}
