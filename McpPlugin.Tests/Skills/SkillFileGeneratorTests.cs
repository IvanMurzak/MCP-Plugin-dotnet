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
using FluentAssertions;
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

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain($"{host}/api/tools/add");
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

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "ping", "SKILL.md"));
            content.Should().Contain($"{host}/api/tools/ping");
        }

        [Fact]
        public void Generate_WithCustomHost_DoesNotContainDifferentHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "query" };
            const string host = "http://production.server.com:4000";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "query", "SKILL.md"));
            // The only host that should appear is the one we passed in
            content.Should().Contain(host);
            content.Should().NotContain("localhost:8080");
        }

        [Fact]
        public void Generate_WithHttpsHost_UsesHttpsInUrls()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "secure-op" };
            const string host = "https://secure.example.com";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "secure-op", "SKILL.md"));
            content.Should().Contain("https://secure.example.com/api/tools/secure-op");
        }

        [Fact]
        public void Generate_BothCurlSnippets_ContainHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "compute" };
            const string host = "http://bridge:8888";

            var result = generator.Generate(new[] { tool }, _tempDir, host);

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "compute", "SKILL.md"));
            // Both the plain and the auth-header curl snippets must use the given host
            var expectedUrl = $"{host}/api/tools/compute";
            content.Split(expectedUrl).Length.Should().BeGreaterThanOrEqualTo(3,
                "because the host URL appears in both curl examples");
        }

        // ── file creation ────────────────────────────────────────────────────────

        [Fact]
        public void Generate_WithSingleTool_CreatesSkillFileInSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "my-tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var expectedFile = Path.Combine(_tempDir, "my-tool", "SKILL.md");
            File.Exists(expectedFile).Should().BeTrue();
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

            result.Should().BeTrue();
            foreach (var tool in tools)
                File.Exists(Path.Combine(_tempDir, tool.Name, "SKILL.md")).Should().BeTrue($"{tool.Name}/SKILL.md should exist");
        }

        [Fact]
        public void Generate_SanitizesTool_CreatesSanitizedSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "My Complex Tool!" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).Should().BeTrue();
        }

        // ── null / empty input guards ─────────────────────────────────────────────

        [Fact]
        public void Generate_WithNullTools_DoesNotThrow()
        {
            var generator = new SkillFileGenerator();

            bool result = false;
            Action act = () => { result = generator.Generate(null!, _tempDir, "http://localhost:8080"); };

            act.Should().NotThrow();
            result.Should().BeFalse();
        }

        [Fact]
        public void Generate_WithNullTools_DoesNotCreateDirectory()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Generate(null!, _tempDir, "http://localhost:8080");

            result.Should().BeFalse();

            // Skills dir should not have been created (or be empty if OS creates it)
            if (Directory.Exists(_tempDir))
                Directory.GetDirectories(_tempDir).Should().BeEmpty();
        }

        [Fact]
        public void Generate_WithEmptyTools_DoesNotCreateAnySubdirectories()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Generate(Array.Empty<IRunTool>(), _tempDir, "http://localhost:8080");

            result.Should().BeTrue();

            if (Directory.Exists(_tempDir))
                Directory.GetDirectories(_tempDir).Should().BeEmpty();
        }

        // ── markdown content ─────────────────────────────────────────────────────

        [Fact]
        public void Generate_WithToolDescription_IncludesDescriptionInMarkdown()
        {
            var generator = new SkillFileGenerator();
            const string description = "Adds two integers and returns the sum.";
            var tool = new MockRunTool { Name = "add", Description = description };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain(description);
        }

        [Fact]
        public void Generate_WithToolTitle_IncludesTitleInMarkdown()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add", Title = "Addition Tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain("# Addition Tool");
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

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain("| `a`");
            content.Should().Contain("| `b`");
            content.Should().Contain("First operand");
            content.Should().Contain("Second operand");
        }

        [Fact]
        public void Generate_WithOutputSchema_IncludesOutputSection()
        {
            var generator = new SkillFileGenerator();
            var outputSchema = JsonNode.Parse("""{"type":"object","properties":{"result":{"type":"integer"}}}""")!;
            var tool = new MockRunTool { Name = "add", OutputSchema = outputSchema };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain("## Output");
            content.Should().Contain("Output JSON Schema");
        }

        [Fact]
        public void Generate_WithNullOutputSchema_ShowsNoStructuredOutput()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "fire-and-forget", OutputSchema = null };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "fire-and-forget", "SKILL.md"));
            content.Should().Contain("does not return structured output");
        }

        [Fact]
        public void Generate_ContainsYamlFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "my-tool", Description = "Test tool" };

            var result = generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            result.Should().BeTrue();
            var content = File.ReadAllText(Path.Combine(_tempDir, "my-tool", "SKILL.md"));
            content.Should().StartWith("---");
            content.Should().Contain("name: my-tool");
        }

        // ── delete ───────────────────────────────────────────────────────────────

        [Fact]
        public void Delete_WithNullTools_DoesNotThrow()
        {
            var generator = new SkillFileGenerator();

            bool result = false;
            Action act = () => { result = generator.Delete(null!, _tempDir); };

            act.Should().NotThrow();
            result.Should().BeFalse();
        }

        [Fact]
        public void Delete_WithEmptyTools_ReturnsTrue()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Delete(Array.Empty<IRunTool>(), _tempDir);

            result.Should().BeTrue();
        }

        [Fact]
        public void Delete_WhenSkillsDirDoesNotExist_ReturnsTrue()
        {
            var generator = new SkillFileGenerator();
            var nonExistentDir = Path.Combine(_tempDir, "does-not-exist");

            var result = generator.Delete(new[] { new MockRunTool { Name = "add" } }, nonExistentDir);

            result.Should().BeTrue();
        }

        [Fact]
        public void Delete_WithSingleTool_RemovesItsSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add" };
            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");
            Directory.Exists(Path.Combine(_tempDir, "add")).Should().BeTrue();

            var result = generator.Delete(new[] { tool }, _tempDir);

            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_tempDir, "add")).Should().BeFalse();
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

            result.Should().BeTrue();
            foreach (var tool in tools)
                Directory.Exists(Path.Combine(_tempDir, tool.Name)).Should().BeFalse($"{tool.Name}/ should have been deleted");
        }

        [Fact]
        public void Delete_LeavesUnrelatedSubdirectoriesIntact()
        {
            var generator = new SkillFileGenerator();
            var toolToDelete = new MockRunTool { Name = "remove-me" };
            var toolToKeep = new MockRunTool { Name = "keep-me" };
            generator.Generate(new[] { toolToDelete, toolToKeep }, _tempDir, "http://localhost:8080");

            var result = generator.Delete(new[] { toolToDelete }, _tempDir);

            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_tempDir, "remove-me")).Should().BeFalse();
            Directory.Exists(Path.Combine(_tempDir, "keep-me")).Should().BeTrue("unrelated skill dir must not be touched");
        }

        [Fact]
        public void Delete_WhenToolDirDoesNotExist_StillReturnsTrue()
        {
            var generator = new SkillFileGenerator();
            Directory.CreateDirectory(_tempDir);
            // Do not generate — the subdirectory for "ghost-tool" never exists

            var result = generator.Delete(new[] { new MockRunTool { Name = "ghost-tool" } }, _tempDir);

            result.Should().BeTrue();
        }

        [Fact]
        public void Delete_WithSanitizedName_RemovesSanitizedSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "My Complex Tool!" };
            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).Should().BeTrue();

            var result = generator.Delete(new[] { tool }, _tempDir);

            result.Should().BeTrue();
            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).Should().BeFalse();
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
            Directory.GetDirectories(_tempDir).Should().HaveCount(2, "each colliding tool gets its own hashed directory");

            // Delete must receive the same tool set so it can reproduce the same collision/hash logic
            var result = generator.Delete(tools, _tempDir);

            result.Should().BeTrue();
            Directory.GetDirectories(_tempDir).Should().BeEmpty("both hashed dirs should have been deleted");
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
    }
}
