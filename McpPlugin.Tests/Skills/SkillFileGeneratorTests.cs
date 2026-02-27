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

            generator.Generate(new[] { tool }, _tempDir, host);

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

            generator.Generate(new[] { tool }, _tempDir, host);

            var content = File.ReadAllText(Path.Combine(_tempDir, "ping", "SKILL.md"));
            content.Should().Contain($"{host}/api/tools/ping");
        }

        [Fact]
        public void Generate_WithCustomHost_DoesNotContainDifferentHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "query" };
            const string host = "http://production.server.com:4000";

            generator.Generate(new[] { tool }, _tempDir, host);

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

            generator.Generate(new[] { tool }, _tempDir, host);

            var content = File.ReadAllText(Path.Combine(_tempDir, "secure-op", "SKILL.md"));
            content.Should().Contain("https://secure.example.com/api/tools/secure-op");
        }

        [Fact]
        public void Generate_BothCurlSnippets_ContainHost()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "compute" };
            const string host = "http://bridge:8888";

            generator.Generate(new[] { tool }, _tempDir, host);

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

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

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

            generator.Generate(tools, _tempDir, "http://localhost:8080");

            foreach (var tool in tools)
                File.Exists(Path.Combine(_tempDir, tool.Name, "SKILL.md")).Should().BeTrue($"{tool.Name}/SKILL.md should exist");
        }

        [Fact]
        public void Generate_SanitizesTool_CreatesSanitizedSubdirectory()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "My Complex Tool!" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            Directory.Exists(Path.Combine(_tempDir, "my-complex-tool")).Should().BeTrue();
        }

        // ── null / empty input guards ─────────────────────────────────────────────

        [Fact]
        public void Generate_WithNullTools_DoesNotThrow()
        {
            var generator = new SkillFileGenerator();

            Action act = () => generator.Generate(null!, _tempDir, "http://localhost:8080");

            act.Should().NotThrow();
        }

        [Fact]
        public void Generate_WithNullTools_DoesNotCreateDirectory()
        {
            var generator = new SkillFileGenerator();

            generator.Generate(null!, _tempDir, "http://localhost:8080");

            // Skills dir should not have been created (or be empty if OS creates it)
            if (Directory.Exists(_tempDir))
                Directory.GetDirectories(_tempDir).Should().BeEmpty();
        }

        [Fact]
        public void Generate_WithEmptyTools_DoesNotCreateAnySubdirectories()
        {
            var generator = new SkillFileGenerator();

            generator.Generate(Array.Empty<IRunTool>(), _tempDir, "http://localhost:8080");

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

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain(description);
        }

        [Fact]
        public void Generate_WithToolTitle_IncludesTitleInMarkdown()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "add", Title = "Addition Tool" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

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

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

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

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "add", "SKILL.md"));
            content.Should().Contain("## Output");
            content.Should().Contain("Output JSON Schema");
        }

        [Fact]
        public void Generate_WithNullOutputSchema_ShowsNoStructuredOutput()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "fire-and-forget", OutputSchema = null };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "fire-and-forget", "SKILL.md"));
            content.Should().Contain("does not return structured output");
        }

        [Fact]
        public void Generate_ContainsYamlFrontMatter()
        {
            var generator = new SkillFileGenerator();
            var tool = new MockRunTool { Name = "my-tool", Description = "Test tool" };

            generator.Generate(new[] { tool }, _tempDir, "http://localhost:8080");

            var content = File.ReadAllText(Path.Combine(_tempDir, "my-tool", "SKILL.md"));
            content.Should().StartWith("---");
            content.Should().Contain("name: my-tool");
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
