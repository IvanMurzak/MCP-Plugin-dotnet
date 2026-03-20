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
using System.IO;
using com.IvanMurzak.McpPlugin.Skills;
using com.IvanMurzak.McpPlugin.Tests.Data.Annotations;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpPluginSkillContentTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();
        private readonly string _tempDir;

        public McpPluginSkillContentTests(ITestOutputHelper output)
        {
            _output = output;
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
            _tempDir = Path.Combine(Path.GetTempPath(), "McpPluginSkillContentTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private McpPluginBuilder BuildWithSkills()
        {
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithSkills(typeof(AnnotatedSkillClass));
            builder.Build(new Reflector());
            return builder;
        }

        // ── WithSkills() explicit type registration ─────────────────────────

        [Fact]
        public void WithSkills_ExplicitType_DiscoversConstStringFieldsWithAttribute()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ShouldNotBeNull();
            collection.ContainsKey("deploy-guide").ShouldBeTrue();
            collection.ContainsKey("troubleshoot").ShouldBeTrue();
        }

        [Fact]
        public void WithSkills_ExplicitType_SkillContentHasCorrectValues()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            var deployGuide = collection["deploy-guide"];

            deployGuide.Name.ShouldBe("deploy-guide");
            deployGuide.Description.ShouldBe("Step-by-step deployment instructions");
            deployGuide.Content.ShouldContain("Build the project");
            deployGuide.Content.ShouldContain("Run migrations");
            deployGuide.Enabled.ShouldBeTrue();
        }

        // ── Disabled skills excluded ────────────────────────────────────────

        [Fact]
        public void WithSkills_DisabledSkill_IsExcludedFromCollection()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("disabled-skill").ShouldBeFalse();
        }

        // ── Non-const and non-string fields ignored ─────────────────────────

        [Fact]
        public void WithSkills_NonConstFieldsWithoutAttribute_AreIgnored()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            // 2 enabled const fields + 1 enabled property = 3 skills
            collection.Count.ShouldBe(3);
        }

        // ── Static property support ─────────────────────────────────────────

        [Fact]
        public void WithSkills_StaticProperty_IsDiscovered()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("platform-info").ShouldBeTrue();
        }

        [Fact]
        public void WithSkills_StaticProperty_HasCorrectContent()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            var skill = collection["platform-info"];

            skill.Name.ShouldBe("platform-info");
            skill.Description.ShouldBe("Platform-specific instructions");
            skill.Content.ShouldNotBeNullOrWhiteSpace();
            skill.Enabled.ShouldBeTrue();
        }

        [Fact]
        public void WithSkills_DisabledProperty_IsExcluded()
        {
            var builder = BuildWithSkills();

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("disabled-prop").ShouldBeFalse();
        }

        // ── WithSkillsFromAssembly discovers [McpPluginSkillType] classes ───

        [Fact]
        public void WithSkillsFromAssembly_DiscoversAnnotatedSkillClasses()
        {
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithSkillsFromAssembly(typeof(AnnotatedSkillClass).Assembly);
            builder.Build(new Reflector());

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("deploy-guide").ShouldBeTrue();
            collection.ContainsKey("troubleshoot").ShouldBeTrue();
            collection.ContainsKey("platform-info").ShouldBeTrue();
        }

        // ── SKILL.md generation ─────────────────────────────────────────────

        [Fact]
        public void Generate_SkillContent_CreatesCorrectSkillFiles()
        {
            var generator = new SkillFileGenerator();
            var skills = new[]
            {
                new SkillContent("my-skill", "A custom skill", "# My Skill\n\nDo stuff.\n")
            };

            var result = generator.Generate(skills, _tempDir);

            result.ShouldBeTrue();
            var skillDir = Path.Combine(_tempDir, "my-skill");
            Directory.Exists(skillDir).ShouldBeTrue();

            var filePath = Path.Combine(skillDir, "SKILL.md");
            File.Exists(filePath).ShouldBeTrue();

            var content = File.ReadAllText(filePath);
            content.ShouldContain("---");
            content.ShouldContain("name: my-skill");
            content.ShouldContain("description: A custom skill");
            content.ShouldContain("# My Skill");
            content.ShouldContain("Do stuff.");
        }

        [Fact]
        public void Generate_SkillContent_YamlFrontmatterIsCorrect()
        {
            var generator = new SkillFileGenerator();
            var skills = new[]
            {
                new SkillContent("test-skill", "Test description", "Body content")
            };

            generator.Generate(skills, _tempDir);

            var content = File.ReadAllText(Path.Combine(_tempDir, "test-skill", "SKILL.md"));
            var lines = content.Split('\n');
            lines[0].Trim().ShouldBe("---");
            lines[1].Trim().ShouldBe("name: test-skill");
            lines[2].Trim().ShouldBe("description: Test description");
            lines[3].Trim().ShouldBe("---");
        }

        // ── Deletion ────────────────────────────────────────────────────────

        [Fact]
        public void Delete_SkillContent_RemovesSkillDirectories()
        {
            var generator = new SkillFileGenerator();
            var skills = new[]
            {
                new SkillContent("del-skill", "Delete me", "Content")
            };

            generator.Generate(skills, _tempDir);
            Directory.Exists(Path.Combine(_tempDir, "del-skill")).ShouldBeTrue();

            var result = generator.Delete(skills, _tempDir);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "del-skill")).ShouldBeFalse();
        }

        // ── Name sanitization ───────────────────────────────────────────────

        [Fact]
        public void Generate_SkillContent_SanitizesName()
        {
            var generator = new SkillFileGenerator();
            var skills = new[]
            {
                new SkillContent("My Cool Skill!", "Desc", "Body")
            };

            generator.Generate(skills, _tempDir);

            Directory.Exists(Path.Combine(_tempDir, "my-cool-skill")).ShouldBeTrue();
        }

        // ── Tools and skills coexist ────────────────────────────────────────

        [Fact]
        public void Generate_ToolsAndSkills_CoexistInSameDirectory()
        {
            var generator = new SkillFileGenerator();

            // Create a subdirectory pretending to be a tool's skill
            var toolDir = Path.Combine(_tempDir, "existing-tool");
            Directory.CreateDirectory(toolDir);
            File.WriteAllText(Path.Combine(toolDir, "SKILL.md"), "tool content");

            // Generate a custom skill
            var skills = new[]
            {
                new SkillContent("custom-skill", "Custom", "# Custom\nBody")
            };
            generator.Generate(skills, _tempDir);

            // Both should exist
            Directory.Exists(Path.Combine(_tempDir, "existing-tool")).ShouldBeTrue();
            Directory.Exists(Path.Combine(_tempDir, "custom-skill")).ShouldBeTrue();
        }

        // ── Ignore config applies to skill scanning ─────────────────────────

        [Fact]
        public void WithSkillsFromAssembly_IgnoredNamespace_SkillsAreExcluded()
        {
            var builder = new McpPluginBuilder(_version, _loggerProvider);
            builder
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .IgnoreNamespace("com.IvanMurzak.McpPlugin.Tests.Data.Annotations")
                .WithSkillsFromAssembly(typeof(AnnotatedSkillClass).Assembly);
            builder.Build(new Reflector());

            var collection = builder.ServiceProvider!.GetRequiredService<SkillContentCollection>();
            collection.ContainsKey("deploy-guide").ShouldBeFalse();
            collection.ContainsKey("troubleshoot").ShouldBeFalse();
        }

        // ── Null/empty edge cases ───────────────────────────────────────────

        [Fact]
        public void Generate_SkillContent_NullCollection_ReturnsFalse()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Generate((ISkillContent[])null!, _tempDir);

            result.ShouldBeFalse();
        }

        [Fact]
        public void Delete_SkillContent_NullCollection_ReturnsFalse()
        {
            var generator = new SkillFileGenerator();

            var result = generator.Delete((ISkillContent[])null!, _tempDir);

            result.ShouldBeFalse();
        }

        [Fact]
        public void Delete_SkillContent_NonExistentDirectory_ReturnsTrue()
        {
            var generator = new SkillFileGenerator();
            var skills = new[]
            {
                new SkillContent("no-dir", "Test", "Body")
            };

            var result = generator.Delete(skills, Path.Combine(_tempDir, "nonexistent"));

            result.ShouldBeTrue();
        }
    }
}
