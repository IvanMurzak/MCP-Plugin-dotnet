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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Skills;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace com.IvanMurzak.McpPlugin.Tests.Skills
{
    public class SkillFileGeneratorTests : IDisposable
    {
        readonly string _tempDir;
        readonly ITestOutputHelper _output;

        public SkillFileGeneratorTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), $"SkillFileGeneratorTests_{Guid.NewGuid():N}");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── host in URL ──────────────────────────────────────────────────────────

        [Fact]
        public void Generate_WithHost_WritesHostToUrls()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add" };
            const string host = "http://myapp.example.com:9000";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.ShouldContain($"{host}/api/tools/add");
        }

        [Theory]
        [InlineData("http://localhost:8080")]
        [InlineData("http://myserver.internal:5000")]
        [InlineData("https://api.example.com")]
        [InlineData("https://192.168.1.42:7443")]
        public void Generate_WithVariousHosts_EachAppearsInUrl(string host)
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "ping" };

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "ping", "SKILL.md"));
            content.ShouldContain($"{host}/api/tools/ping");
        }

        [Fact]
        public void Generate_WithCustomHost_DoesNotContainDifferentHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "query" };
            const string host = "http://production.server.com:4000";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "query", "SKILL.md"));
            // The only host that should appear is the one we passed in
            content.ShouldContain(host);
            content.ShouldNotContain("localhost:8080");
        }

        [Fact]
        public void Generate_WithHttpsHost_UsesHttpsInUrls()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "secure-op" };
            const string host = "https://secure.example.com";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "secure-op", "SKILL.md"));
            content.ShouldContain("https://secure.example.com/api/tools/secure-op");
        }

        [Fact]
        public void Generate_BothCurlSnippets_ContainHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "compute" };
            const string host = "http://bridge:8888";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "compute", "SKILL.md"));
            // Both the plain and the auth-header curl snippets must use the given host
            var expectedUrl = $"{host}/api/tools/compute";
            content.Split(expectedUrl).Length.ShouldBeGreaterThanOrEqualTo(3,
                "because the host URL appears in both curl examples");
        }

        // ── file creation ────────────────────────────────────────────────────────

        [Fact]
        public void Generate_WithSingleTool_CreatesSkillFileInSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "my-tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var expectedFile = Path.Combine(_tempDir, "my-tool", "SKILL.md");
            File.Exists(expectedFile).ShouldBeTrue();
        }

        [Fact]
        public void Generate_WithMultipleTools_CreatesFileForEach()
        {
            var generator = new SkillFileGenerator();
            var tools = new[]
            {
                new MockRunTool { Name = "tool-alpha" },
                new MockRunTool { Name = "tool-beta" },
                new MockRunTool { Name = "tool-gamma" }
            };

            var result = generator.Generate(tools, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            foreach (var tool in tools)
                File.Exists(Path.Combine(_tempDir, tool.Name, "SKILL.md")).ShouldBeTrue($"{tool.Name}/SKILL.md should exist");
        }

        [Fact]
        public void Generate_SanitizesTool_CreatesSanitizedSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "My Complex Tool!" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).ShouldBeTrue();
        }

        // ── null / empty input guards ─────────────────────────────────────────────

        [Fact]
        public void Generate_WithNullTools_DoesNotThrow()
        {
            var generator = new SkillFileGenerator();

            bool result = false;
            Action act = () => { result = generator.Generate(null!, _tempDir, "http://localhost:8080"); };

            Should.NotThrow(act);
            result.ShouldBeFalse();
        }

        [Fact]
        public void Generate_WithNullTools_DoesNotCreateDirectory()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Generate(null!, _tempDir, "http://localhost:8080");

            result.ShouldBeFalse();

            // Skills dir should not have been created (or be empty if OS creates it)
            if (Directory.Exists(_tempDir))
                Directory.GetDirectories(_tempDir).ShouldBeEmpty();
        }

        [Fact]
        public void Generate_WithEmptyTools_DoesNotCreateAnySubdirectories()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Generate(Array.Empty<IRunTool>(), _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();

            if (Directory.Exists(_tempDir))
                Directory.GetDirectories(_tempDir).ShouldBeEmpty();
        }

        // ── markdown content ─────────────────────────────────────────────────────

        [Fact]
        public void Generate_WithToolDescription_IncludesDescriptionInMarkdown()
        {
            var generator = new SkillFileGenerator();
            const string description = "Adds two integers and returns the sum.";
            var tool = new MockRunTool { Name = "add", Description = description };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.ShouldContain(description);
        }

        [Fact]
        public void Generate_WithToolTitle_IncludesTitleInMarkdown()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add", Title = "Addition Tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.ShouldContain("# Addition Tool");
        }

        [Fact]
        public void Generate_WithInputSchema_IncludesParameterTable()
        {
            var generator = new SkillFileGenerator();
            var schema = JsonNode.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "a": { "type": "integer", "description": "First operand" },
                        "b": { "type": "integer", "description": "Second operand" }
                    },
                    "required": ["a", "b"]
                }
                """)!;
            var tool = new MockRunTool { Name = "add", InputSchema = schema };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.ShouldContain("| `a`");
            content.ShouldContain("| `b`");
            content.ShouldContain("First operand");
            content.ShouldContain("Second operand");
        }

        [Fact]
        public void Generate_WithOutputSchema_IncludesOutputSection()
        {
            var generator = new SkillFileGenerator();
            var outputSchema = JsonNode.Parse("""{"type":"object","properties":{"result":{"type":"integer"}}}""")!;
            var tool = new MockRunTool { Name = "add", OutputSchema = outputSchema };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.ShouldContain("## Output");
            content.ShouldContain("Output JSON Schema");
        }

        [Fact]
        public void Generate_WithNullOutputSchema_ShowsNoStructuredOutput()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "fire-and-forget", OutputSchema = null };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "fire-and-forget", "SKILL.md"));
            content.ShouldContain("does not return structured output");
        }

        [Fact]
        public void Generate_ContainsYamlFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "my-tool", Description = "Test tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.ShouldBeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "my-tool", "SKILL.md"));
            content.ShouldStartWith("---");
            content.ShouldContain("name: my-tool");
        }

        // ── delete ───────────────────────────────────────────────────────────────

        [Fact]
        public void Delete_WithNullTools_DoesNotThrow()
        {
            var generator = new SkillFileGenerator();

            bool result = false;
            Action act = () => { result = generator.Delete(null!, _tempDir); };

            Should.NotThrow(act);
            result.ShouldBeFalse();
        }

        [Fact]
        public void Delete_WithEmptyTools_ReturnsTrue()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Delete(Array.Empty<IRunTool>(), _tempDir);

            result.ShouldBeTrue();
        }

        [Fact]
        public void Delete_WhenSkillsDirDoesNotExist_ReturnsTrue()
        {
            var generator = new SkillFileGenerator();
            var nonExistentDir = Path.Combine(_tempDir, "does-not-exist");

            var result = generator.Delete(new[] { new MockRunTool { Name = "add" } }, nonExistentDir);

            result.ShouldBeTrue();
        }

        [Fact]
        public void Delete_WithSingleTool_RemovesItsSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add" };
            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");
            Directory.Exists(Path.Combine(_tempDir, "add")).ShouldBeTrue();

            var result = generator.Delete(new[] { tool }, _tempDir);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "add")).ShouldBeFalse();
        }

        [Fact]
        public void Delete_WithMultipleTools_RemovesEachSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tools = new[]
            {
                new MockRunTool { Name = "tool-alpha" },
                new MockRunTool { Name = "tool-beta" },
                new MockRunTool { Name = "tool-gamma" }
            };
            generator.Generate(tools, _tempDir, "http://localhost:8080");

            var result = generator.Delete(tools, _tempDir);

            result.ShouldBeTrue();
            foreach (var tool in tools)
                Directory.Exists(Path.Combine(_tempDir, tool.Name)).ShouldBeFalse($"{tool.Name}/ should have been deleted");
        }

        [Fact]
        public void Delete_LeavesUnrelatedSubdirectoriesIntact()
        {
            var generator = new SkillFileGenerator();
            var toolToDelete = new MockRunTool { Name = "remove-me" };
            var toolToKeep = new MockRunTool { Name = "keep-me" };
            generator.Generate(new[] { toolToDelete, toolToKeep }, _tempDir, "http://localhost:8080");

            var result = generator.Delete(new[] { toolToDelete }, _tempDir);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "remove-me")).ShouldBeFalse();
            Directory.Exists(Path.Combine(_tempDir, "keep-me")).ShouldBeTrue("unrelated skill dir must not be touched");
        }

        [Fact]
        public void Delete_WhenToolDirDoesNotExist_StillReturnsTrue()
        {
            var generator = new SkillFileGenerator();
            Directory.CreateDirectory(_tempDir);
            // Do not generate — the subdirectory for "ghost-tool" never exists

            var result = generator.Delete(new[] { new MockRunTool { Name = "ghost-tool" } }, _tempDir);

            result.ShouldBeTrue();
        }

        [Fact]
        public void Delete_WithSanitizedName_RemovesSanitizedSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "My Complex Tool!" };
            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).ShouldBeTrue();

            var result = generator.Delete(new[] { tool }, _tempDir);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).ShouldBeFalse();
        }

        [Fact]
        public void Delete_WithCollidingNames_RemovesBothHashedSubdirectories()
        {
            var generator = new SkillFileGenerator();
            // "foo bar" and "foo-bar" both sanitize to "foo-bar" → collision → hashed dirs
            var toolA = new MockRunTool { Name = "foo bar" };
            var toolB = new MockRunTool { Name = "foo-bar" };
            var tools = new[] { toolA, toolB };
            generator.Generate(tools, _tempDir, "http://localhost:8080");

            // Both hashed dirs should exist
            Directory.GetDirectories(_tempDir).Length.ShouldBe(2, "each colliding tool gets its own hashed directory");

            // Delete must receive the same tool set so it can reproduce the same collision/hash logic
            var result = generator.Delete(tools, _tempDir);

            result.ShouldBeTrue();
            Directory.GetDirectories(_tempDir).ShouldBeEmpty("both hashed dirs should have been deleted");
        }

        // ── virtual bool properties ──────────────────────────────────────────────

        [Fact]
        public void IncludeAuthorizationExample_Default_ContainsAuthorizationBlock()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain("Authorization: Bearer");
        }

        [Fact]
        public void Generate_IncludeAuthorizationExampleFalse_OmitsAuthorizationBlock()
        {
            var generator = new CustomFlagsGenerator(includeAuth: false);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldNotContain("Authorization: Bearer");
        }

        [Fact]
        public void Generate_IncludeParameterTableFalse_OmitsParameterTable()
        {
            var schema = JsonNode.Parse("""{"type":"object","properties":{"x":{"type":"integer","description":"X value"}}}""")!;
            var generator = new CustomFlagsGenerator(includeParamTable: false);
            var tool = new MockRunTool { Name = "calc", InputSchema = schema };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "calc", "SKILL.md"));
            content.ShouldNotContain("| Name | Type |");
            content.ShouldNotContain("| `x`");
        }

        [Fact]
        public void Generate_IncludeInputJsonSchemaFalse_OmitsInputJsonSchemaBlock()
        {
            var schema = JsonNode.Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""")!;
            var generator = new CustomFlagsGenerator(includeInputJsonSchema: false);
            var tool = new MockRunTool { Name = "calc", InputSchema = schema };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "calc", "SKILL.md"));
            content.ShouldNotContain("### Input JSON Schema");
        }

        [Fact]
        public void Generate_IncludeOutputSectionFalse_OmitsOutputSection()
        {
            var generator = new CustomFlagsGenerator(includeOutput: false);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldNotContain("## Output");
        }

        // ── additional content injection ─────────────────────────────────────────

        [Fact]
        public void GetAdditionalContent_Default_ReturnsNull()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "op" };

            generator.GetAdditionalContent(tool).ShouldBeNull();
        }

        [Fact]
        public void Generate_AdditionalContent_End_AppearsAfterOutputSection()
        {
            const string marker = "CUSTOM_END_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.End);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(marker);
            var outputIdx = content.IndexOf("## Output", StringComparison.Ordinal);
            var markerIdx = content.IndexOf(marker, StringComparison.Ordinal);
            markerIdx.ShouldBeGreaterThan(outputIdx, "additional content at End should appear after the Output section");
        }

        [Fact]
        public void Generate_AdditionalContent_AfterTitle_AppearsBetweenTitleAndHowToCall()
        {
            const string marker = "CUSTOM_AFTER_TITLE_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.AfterTitle);
            var tool = new MockRunTool { Name = "op", Title = "My Op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(marker);
            var titleIdx = content.IndexOf("# My Op", StringComparison.Ordinal);
            var markerIdx = content.IndexOf(marker, StringComparison.Ordinal);
            var howToCallIdx = content.IndexOf("## How to Call", StringComparison.Ordinal);
            markerIdx.ShouldBeGreaterThan(titleIdx, "additional content should appear after the title");
            markerIdx.ShouldBeLessThan(howToCallIdx, "additional content should appear before 'How to Call'");
        }

        [Fact]
        public void Generate_AdditionalContent_AfterHowToCall_AppearsBetweenHowToCallAndInput()
        {
            const string marker = "CUSTOM_AFTER_HOWTO_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.AfterHowToCall);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(marker);
            var howToCallIdx = content.IndexOf("## How to Call", StringComparison.Ordinal);
            var markerIdx = content.IndexOf(marker, StringComparison.Ordinal);
            var inputIdx = content.IndexOf("## Input", StringComparison.Ordinal);
            markerIdx.ShouldBeGreaterThan(howToCallIdx, "additional content should appear after 'How to Call'");
            markerIdx.ShouldBeLessThan(inputIdx, "additional content should appear before 'Input'");
        }

        [Fact]
        public void Generate_AdditionalContent_AfterInput_AppearsBetweenInputAndOutput()
        {
            const string marker = "CUSTOM_AFTER_INPUT_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.AfterInput);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(marker);
            var inputIdx = content.IndexOf("## Input", StringComparison.Ordinal);
            var markerIdx = content.IndexOf(marker, StringComparison.Ordinal);
            var outputIdx = content.IndexOf("## Output", StringComparison.Ordinal);
            markerIdx.ShouldBeGreaterThan(inputIdx, "additional content should appear after 'Input'");
            markerIdx.ShouldBeLessThan(outputIdx, "additional content should appear before 'Output'");
        }

        [Fact]
        public void Generate_AdditionalContent_NonePosition_ContentNotInjected()
        {
            const string marker = "CUSTOM_NONE_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.None);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldNotContain(marker); // None position must suppress content injection entirely
        }

        [Fact]
        public void Generate_AdditionalContent_AppearsExactlyOnce()
        {
            const string marker = "CUSTOM_ONCE_MARKER";
            var generator = new AdditionalContentGenerator(marker, SkillAdditionalContentPosition.AfterTitle);
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.Split(marker).Length.ShouldBe(2, "additional content must appear exactly once");
        }

        // ── protected virtual method overrides ───────────────────────────────────

        [Fact]
        public void Generate_OverriddenBuildMarkdown_WritesCustomContent()
        {
            var generator = new CustomMarkdownGenerator();
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldBe(CustomMarkdownGenerator.CustomContent);
        }

        [Fact]
        public void Generate_OverriddenAppendParameterTable_WritesCustomTable()
        {
            var schema = JsonNode.Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""")!;
            var generator = new CustomTableGenerator();
            var tool = new MockRunTool { Name = "op", InputSchema = schema };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(CustomTableGenerator.CustomRow);
            content.ShouldNotContain("| `x`"); // default table rows should be replaced by the override
        }

        [Fact]
        public void Generate_OverriddenBuildInputExample_WritesCustomExample()
        {
            var schema = JsonNode.Parse("""{"type":"object","properties":{"x":{"type":"integer"}}}""")!;
            var generator = new CustomExampleGenerator();
            var tool = new MockRunTool { Name = "op", InputSchema = schema };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "op", "SKILL.md"));
            content.ShouldContain(CustomExampleGenerator.CustomExample);
        }

        [Fact]
        public void Generate_OverriddenGenerateFor_CreatesCustomFile()
        {
            var generator = new CustomGenerateForGenerator();
            var tool = new MockRunTool { Name = "op" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var customFile = Path.Combine(_tempDir, "custom-op", "SKILL.md");
            File.Exists(customFile).ShouldBeTrue("overridden GenerateFor should write to a custom directory");
        }

        [Fact]
        public void Generate_OverriddenBuildNameMap_UsesCustomDirectoryName()
        {
            var generator = new CustomNameMapGenerator();
            var tool = new MockRunTool { Name = "my-tool" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            Directory.Exists(Path.Combine(_tempDir, "prefixed-my-tool")).ShouldBeTrue(
                "overridden BuildNameMap should produce the custom directory name");
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private class MockRunTool : IRunTool
        {
            public string Name { get; init; } = "mock-tool";
            public string? Title { get; init; } = "Mock Tool";
            public string? Description { get; init; } = "A mock tool for testing.";
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

        /// <summary>Configures individual bool customisation flags via constructor parameters.</summary>
        private sealed class CustomFlagsGenerator : SkillFileGenerator
        {
            private readonly bool _includeAuth;
            private readonly bool _includeParamTable;
            private readonly bool _includeInputJsonSchema;
            private readonly bool _includeOutput;

            public override bool IncludeAuthorizationExample => _includeAuth;
            public override bool IncludeParameterTable => _includeParamTable;
            public override bool IncludeInputJsonSchema => _includeInputJsonSchema;
            public override bool IncludeOutputSection => _includeOutput;

            public CustomFlagsGenerator(
                bool includeAuth = true,
                bool includeParamTable = true,
                bool includeInputJsonSchema = true,
                bool includeOutput = true)
            {
                _includeAuth = includeAuth;
                _includeParamTable = includeParamTable;
                _includeInputJsonSchema = includeInputJsonSchema;
                _includeOutput = includeOutput;
            }
        }

        /// <summary>Returns a fixed content string and position for additional-content injection tests.</summary>
        private sealed class AdditionalContentGenerator : SkillFileGenerator
        {
            private readonly string? _content;
            private readonly SkillAdditionalContentPosition _position;

            public override string? GetAdditionalContent(IRunTool tool) => _content;
            public override SkillAdditionalContentPosition AdditionalContentPosition => _position;

            public AdditionalContentGenerator(string? content, SkillAdditionalContentPosition position)
            {
                _content = content;
                _position = position;
            }
        }

        /// <summary>Overrides BuildMarkdown to return a fixed custom string.</summary>
        private sealed class CustomMarkdownGenerator : SkillFileGenerator
        {
            public const string CustomContent = "# CUSTOM MARKDOWN CONTENT";

            protected override string BuildMarkdown(IRunTool tool, string skillName, string host)
                => CustomContent;
        }

        /// <summary>Overrides AppendParameterTable to emit a single custom marker row.</summary>
        private sealed class CustomTableGenerator : SkillFileGenerator
        {
            public const string CustomRow = "CUSTOM_TABLE_ROW_MARKER";

            protected override void AppendParameterTable(StringBuilder sb, JsonNode? inputSchema)
                => sb.AppendLine(CustomRow);
        }

        /// <summary>Overrides BuildInputExample to return a fixed JSON string.</summary>
        private sealed class CustomExampleGenerator : SkillFileGenerator
        {
            public const string CustomExample = "{\"custom\":\"injected_example\"}";

            protected override string BuildInputExample(JsonNode? inputSchema)
                => CustomExample;
        }

        /// <summary>Overrides GenerateFor to write the skill file into a "custom-" prefixed directory.</summary>
        private sealed class CustomGenerateForGenerator : SkillFileGenerator
        {
            protected override bool GenerateFor(IRunTool tool, string skillsDir, string host, string skillName)
            {
                var customDir = Path.Combine(skillsDir, "custom-" + skillName);
                Directory.CreateDirectory(customDir);
                File.WriteAllText(Path.Combine(customDir, "SKILL.md"), "custom", Encoding.UTF8);
                return true;
            }
        }

        /// <summary>Overrides BuildNameMap to prefix every directory name with "prefixed-".</summary>
        private sealed class CustomNameMapGenerator : SkillFileGenerator
        {
            protected override Dictionary<string, string> BuildNameMap(List<IRunTool> tools, string callerName)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var tool in tools)
                    map[tool.Name] = "prefixed-" + SanitizeSkillName(tool.Name);
                return map;
            }
        }
    }
}
