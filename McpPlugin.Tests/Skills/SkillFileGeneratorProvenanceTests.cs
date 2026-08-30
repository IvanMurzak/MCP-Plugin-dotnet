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
    /// Pins the provenance marker the DEFAULT <see cref="SkillFileGenerator"/> stamps into a
    /// generated SKILL.md front-matter, in BOTH emission paths (tool skills and custom
    /// skill-content skills), plus the two properties a consumer relies on when it uses the marker
    /// to de-duplicate generated skills: regeneration is byte-stable, and a skill file the
    /// generator does not own is never rewritten. A subclass that replaces <c>BuildMarkdown</c> or
    /// <c>BuildSkillContentMarkdown</c> wholesale is outside this contract — see those methods'
    /// own documentation.
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

            MarkerBlockIsExactlyTheProvenanceEntry(frontMatter);
        }

        [Fact]
        public void Generate_SkillContent_StampsMarkerInsideFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var skill = new SkillContent("marked-skill", "A skill.", "# Marked\n\nBody.\n");

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "marked-skill", "SKILL.md"));
            var frontMatter = FrontMatter(lines);

            MarkerBlockIsExactlyTheProvenanceEntry(frontMatter);
        }

        [Fact]
        public void Generate_Tool_StampsMarkerExactlyOnce()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "once-tool" };

            generator.Generate(new[] { tool }, _tempDir, Host).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "once-tool", "SKILL.md"));
            CountOf(lines, MarkerEntryLine).ShouldBe(1, WholeFile("tool path: provenance entry line", lines));
        }

        [Fact]
        public void Generate_SkillContent_StampsMarkerExactlyOnce()
        {
            var generator = new SkillFileGenerator();
            var skill = new SkillContent("once-skill", "A skill.", "Body");

            generator.Generate(new[] { skill }, _tempDir).ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "once-skill", "SKILL.md"));
            CountOf(lines, MarkerEntryLine).ShouldBe(1, WholeFile("skill-content path: provenance entry line", lines));
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
            MarkerBlockIsExactlyTheProvenanceEntry(frontMatter);
        }

        // ── Idempotence ──────────────────────────────────────────────────────────

        [Fact]
        public void Generate_Tool_RegeneratingOverAnExistingDirectoryIsByteStable()
        {
            var filePath = Path.Combine(_tempDir, "stable-tool", "SKILL.md");

            // Regeneration in production is a NEW process: a fresh generator over freshly
            // constructed, equal-valued tool objects. Reusing one generator and one tool instance
            // would let any per-instance or identity-derived content pass as byte-stable here and
            // be unstable in the real world.
            new SkillFileGenerator()
                .Generate(new[] { new MockRunTool { Name = "stable-tool", Description = "A tool." } }, _tempDir, Host)
                .ShouldBeTrue();
            var first = File.ReadAllBytes(filePath);

            new SkillFileGenerator()
                .Generate(new[] { new MockRunTool { Name = "stable-tool", Description = "A tool." } }, _tempDir, Host)
                .ShouldBeTrue();
            var second = File.ReadAllBytes(filePath);

            second.ShouldBe(first);
            // Positive control: the bytes that stayed equal are bytes that carry the marker — an
            // equality over two empty/marker-free files would prove nothing about this task.
            var lines = ReadLines(filePath);
            CountOf(lines, MarkerEntryLine)
                .ShouldBe(1, WholeFile("byte-stability control, tool path: provenance entry line", lines));
        }

        [Fact]
        public void Generate_SkillContent_RegeneratingOverAnExistingDirectoryIsByteStable()
        {
            var filePath = Path.Combine(_tempDir, "stable-skill", "SKILL.md");

            // Fresh generator + freshly constructed, equal-valued skill on each pass — see the
            // tool-path twin above for why reusing one instance would weaken this.
            new SkillFileGenerator()
                .Generate(new[] { new SkillContent("stable-skill", "A skill.", "# Stable\n\nBody.\n") }, _tempDir)
                .ShouldBeTrue();
            var first = File.ReadAllBytes(filePath);

            new SkillFileGenerator()
                .Generate(new[] { new SkillContent("stable-skill", "A skill.", "# Stable\n\nBody.\n") }, _tempDir)
                .ShouldBeTrue();
            var second = File.ReadAllBytes(filePath);

            second.ShouldBe(first);
            var lines = ReadLines(filePath);
            CountOf(lines, MarkerEntryLine)
                .ShouldBe(1, WholeFile("byte-stability control, skill-content path: provenance entry line", lines));
        }

        // ── Foreign skill files are left alone ───────────────────────────────────
        //
        // Scope, so a later reader does not over-read these: the generator writes only into the
        // directory named for the tool/skill it is generating, so what is pinned is that
        // regenerating a DIFFERENT tool does not disturb an unrelated skill's file. The marker is
        // WRITE-ONLY today — nothing in the generator reads it back — so these do NOT establish
        // that a hand-authored file at a COLLIDING name would be spared. Reading the marker to
        // decide ownership is a consumer-side concern and is deliberately not implemented here.

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
            var generated = ReadLines(Path.Combine(_tempDir, "other-tool", "SKILL.md"));
            CountOf(generated, MarkerEntryLine)
                .ShouldBe(1, WholeFile("foreign-file control, tool path: the generator's OWN file", generated));

            // Byte equality against a literal carrying no marker, so it ALREADY establishes that
            // neither the block key nor the entry was stamped here — asserting either separately
            // would add no independently breakable half.
            File.ReadAllBytes(handCraftedPath)
                .ShouldBe(before, $"the hand-crafted SKILL.md at '{handCraftedPath}' was rewritten by a tool that does not own it");
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

            var generated = ReadLines(Path.Combine(_tempDir, "other-skill", "SKILL.md"));
            CountOf(generated, MarkerEntryLine)
                .ShouldBe(1, WholeFile("foreign-file control, skill-content path: the generator's OWN file", generated));

            File.ReadAllBytes(handCraftedPath)
                .ShouldBe(before, $"the hand-crafted SKILL.md at '{handCraftedPath}' was rewritten by a skill that does not own it");
        }

        // ── The published constants describe the bytes actually written ──────────

        [Fact]
        public void ProvenanceConstants_MatchTheBytesWrittenToDisk()
        {
            // Half 1 — the published contract values have not drifted from what downstream readers
            // were told to expect. These are `public const`, so a consumer inlines them at ITS
            // compile time; changing one is a contract change, not a refactor.
            SkillFileGenerator.ProvenanceBlockKey.ShouldBe("metadata", "published constant: block key");
            SkillFileGenerator.ProvenanceKey.ShouldBe("generated-by", "published constant: entry key");
            SkillFileGenerator.ProvenanceValue.ShouldBe("mcp-plugin-dotnet", "published constant: entry value");

            // Half 2 — INDEPENDENT of half 1: the emission still DERIVES from those constants. If
            // the writer stopped using them (a hardcoded literal drifting away from the published
            // value), every literal pin above still passes while the contract is silently broken.
            // Composed from the constants and compared for whole-LINE equality, never containment:
            // a longer on-disk value would satisfy a substring check.
            new SkillFileGenerator()
                .Generate(new[] { new MockRunTool { Name = "contract-tool" } }, _tempDir, Host)
                .ShouldBeTrue();

            var lines = ReadLines(Path.Combine(_tempDir, "contract-tool", "SKILL.md"));
            lines.ShouldContain(
                SkillFileGenerator.ProvenanceBlockKey + ":",
                "the block key on disk is not the one the published constant names");
            lines.ShouldContain(
                "  " + SkillFileGenerator.ProvenanceKey + ": " + SkillFileGenerator.ProvenanceValue,
                "the provenance entry on disk is not the one the published constants compose");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Asserts the front matter carries a top-level <c>metadata:</c> block whose contents are
        /// EXACTLY the provenance entry. Pinning the whole block — not just "the entry is present
        /// and directly follows its key" — is what makes a SECOND nested entry (a version, a
        /// timestamp) visible: such an entry keeps every containment and adjacency check passing
        /// while destroying the byte-stability the constant marker value exists to guarantee.
        /// The block is emitted immediately before the closing <c>---</c> on both paths, so this
        /// also pins that it is the last front-matter block.
        /// </summary>
        static void MarkerBlockIsExactlyTheProvenanceEntry(List<string> frontMatter)
        {
            var blockIdx = frontMatter.IndexOf(MarkerBlockLine);
            blockIdx.ShouldBeGreaterThanOrEqualTo(
                0,
                $"the front matter carries no top-level '{MarkerBlockLine}' line; it was:\n{string.Join("\n", frontMatter)}");

            frontMatter.GetRange(blockIdx, frontMatter.Count - blockIdx).ToArray().ShouldBe(
                new[] { MarkerBlockLine, MarkerEntryLine },
                "the metadata block must hold the provenance entry and nothing else");
        }

        static string WholeFile(string what, string[] lines)
            => $"{what}: expected exactly one matching line; the file was:\n{string.Join("\n", lines)}";

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

        private class MockRunTool : IRunTool
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
