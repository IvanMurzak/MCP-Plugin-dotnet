/*
Ã¢â€Å’Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Â
Ã¢â€â€š  Author: Ivan Murzak (https://github.com/IvanMurzak)                   Ã¢â€â€š
Ã¢â€â€š  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  Ã¢â€â€š
Ã¢â€â€š  Copyright (c) 2025 Ivan Murzak                                        Ã¢â€â€š
Ã¢â€â€š  Licensed under the Apache License, Version 2.0.                       Ã¢â€â€š
Ã¢â€â€š  See the LICENSE file in the project root for more information.        Ã¢â€â€š
Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Ëœ
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class McpPluginSkillPathTests : IDisposable
    {
        // Primary temp directory Ã¢â‚¬â€ cleaned up in Dispose.
        readonly string _tempDir;

        // BaseDirectory paths created as side-effects Ã¢â‚¬â€ tracked for cleanup.
        readonly List<string> _cleanupPaths = new();

        readonly Reflector _reflector = new Reflector();
        readonly Version _version = new Version();

        // A single tool name used across all tests; the sanitized skill dir is the same string.
        const string ToolName = "skill-path-tool";

        public McpPluginSkillPathTests(ITestOutputHelper output)
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"McpPluginSkillPathTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);

            foreach (var path in _cleanupPaths)
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ helpers Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        IMcpPlugin BuildPlugin(ConnectionConfig config)
            => new McpPluginBuilder(_version)
                .SetConfig(config)
                .AddTool(ToolName, new MockRunTool(ToolName))
                .Build(_reflector);

        // Returns the expected SKILL.md path given the resolved skills directory.
        static string SkillFile(string resolvedSkillsDir) =>
            Path.Combine(resolvedSkillsDir, ToolName, "SKILL.md");

        // Registers a path under AppDomain.BaseDirectory for cleanup after each test.
        string TrackBaseDir(string relName)
        {
            var full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relName);
            _cleanupPaths.Add(full);
            return full;
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ GenerateSkillFiles Ã¢â‚¬â€ path resolution Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        [Fact]
        public void GenerateSkillFiles_NullPath_RelativeSkillsPath_CreatesFilesInBaseDirectory()
        {
            // SkillsPath is relative; passing no path should fall back to AppDomain.BaseDirectory.
            var relName = $"skills_{Guid.NewGuid():N}";
            var expectedSkillsDir = TrackBaseDir(relName);

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles();

            result.ShouldBeTrue();
            File.Exists(SkillFile(expectedSkillsDir)).ShouldBeTrue(
                $"path=null + relative SkillsPath Ã¢â€ â€™ BaseDirectory/{relName}");
        }

        [Fact]
        public void GenerateSkillFiles_WithCustomPath_RelativeSkillsPath_CreatesFilesUnderCustomPath()
        {
            // SkillsPath is relative; custom path should be prepended.
            var customBase = Path.Combine(_tempDir, "custom-base");
            const string relName = "SKILLS";

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(Path.Combine(customBase, relName))).ShouldBeTrue(
                $"custom path + relative SkillsPath Ã¢â€ â€™ customBase/{relName}");
        }

        [Fact]
        public void GenerateSkillFiles_NullPath_AbsoluteSkillsPath_CreatesFilesInAbsolutePath()
        {
            // SkillsPath is absolute; null path must not interfere.
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");

            var config = new ConnectionConfig { SkillsPath = absoluteSkillsDir, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles();

            result.ShouldBeTrue();
            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue(
                "path=null + absolute SkillsPath Ã¢â€ â€™ SkillsPath itself");
        }

        [Fact]
        public void GenerateSkillFiles_WithCustomPath_AbsoluteSkillsPath_IgnoresCustomPath()
        {
            // SkillsPath is absolute; custom path must be completely ignored.
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");
            var customBase = Path.Combine(_tempDir, "should-be-ignored");

            var config = new ConnectionConfig { SkillsPath = absoluteSkillsDir, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue(
                "absolute SkillsPath takes precedence over custom path");
            Directory.Exists(customBase).ShouldBeFalse(
                "custom path must never be touched when SkillsPath is absolute");
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ GenerateSkillFilesIfNeeded Ã¢â‚¬â€ flag + path Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        [Fact]
        public void GenerateSkillFilesIfNeeded_WhenDisabled_ReturnsFalse_NoFilesCreated()
        {
            // Even when a custom path is provided, the disabled flag must short-circuit everything.
            var customBase = Path.Combine(_tempDir, "disabled-test");
            const string relName = "SKILLS";

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFilesIfNeeded(customBase);

            result.ShouldBeFalse("generation is disabled via config");
            Directory.Exists(customBase).ShouldBeFalse(
                "no directory must be created when generation is disabled");
        }

        [Fact]
        public void GenerateSkillFilesIfNeeded_WhenEnabled_NullPath_RelativeSkillsPath_CreatesFilesInBaseDirectory()
        {
            // When enabled and path=null, files must land in BaseDirectory/relName.
            // The constructor also calls GenerateSkillFilesIfNeeded(), so we delete files first
            // and then re-call to isolate the explicit invocation's behavior.
            var relName = $"skills_{Guid.NewGuid():N}";
            var expectedSkillsDir = TrackBaseDir(relName);

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = true };
            using var plugin = BuildPlugin(config);

            // Constructor already created files; delete them to make the next call non-trivially testable.
            if (Directory.Exists(expectedSkillsDir))
                Directory.Delete(expectedSkillsDir, recursive: true);

            var result = plugin.GenerateSkillFilesIfNeeded();

            result.ShouldBeTrue();
            File.Exists(SkillFile(expectedSkillsDir)).ShouldBeTrue(
                $"path=null + enabled + relative SkillsPath Ã¢â€ â€™ BaseDirectory/{relName}");
        }

        [Fact]
        public void GenerateSkillFilesIfNeeded_WhenEnabled_WithCustomPath_RelativeSkillsPath_CreatesFilesUnderCustomPath()
        {
            // When enabled and a custom path is supplied, files must land at customBase/relName.
            var relName = $"skills_{Guid.NewGuid():N}";
            TrackBaseDir(relName); // ctor creates files here with null path; register for cleanup

            var customBase = Path.Combine(_tempDir, "custom-base");

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = true };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFilesIfNeeded(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(Path.Combine(customBase, relName))).ShouldBeTrue(
                $"custom path + enabled + relative SkillsPath Ã¢â€ â€™ customBase/{relName}");
        }

        [Fact]
        public void GenerateSkillFilesIfNeeded_WhenEnabled_WithCustomPath_AbsoluteSkillsPath_IgnoresCustomPath()
        {
            // Absolute SkillsPath must win over any supplied base path.
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");
            var customBase = Path.Combine(_tempDir, "should-be-ignored");

            var config = new ConnectionConfig { SkillsPath = absoluteSkillsDir, GenerateSkillFiles = true };
            using var plugin = BuildPlugin(config); // ctor creates in absoluteSkillsDir

            var result = plugin.GenerateSkillFilesIfNeeded(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue(
                "absolute SkillsPath takes precedence over custom path");
            Directory.Exists(customBase).ShouldBeFalse(
                "custom path must never be touched when SkillsPath is absolute");
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ DeleteSkillFiles Ã¢â‚¬â€ path resolution Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        [Fact]
        public void DeleteSkillFiles_NullPath_RelativeSkillsPath_DeletesFromBaseDirectory()
        {
            var relName = $"skills_{Guid.NewGuid():N}";
            var skillsDir = TrackBaseDir(relName);

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            plugin.GenerateSkillFiles(); // create files at BaseDirectory/relName

            File.Exists(SkillFile(skillsDir)).ShouldBeTrue("setup: SKILL.md must exist before delete");

            var result = plugin.DeleteSkillFiles();

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(skillsDir, ToolName)).ShouldBeFalse(
                "path=null + relative SkillsPath Ã¢â€ â€™ delete from BaseDirectory/relName");
        }

        [Fact]
        public void DeleteSkillFiles_WithCustomPath_RelativeSkillsPath_DeletesFromCustomPath()
        {
            var customBase = Path.Combine(_tempDir, "custom-base");
            const string relName = "SKILLS";
            var skillsDir = Path.Combine(customBase, relName);

            var config = new ConnectionConfig { SkillsPath = relName, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            plugin.GenerateSkillFiles(customBase); // create files at customBase/SKILLS

            File.Exists(SkillFile(skillsDir)).ShouldBeTrue("setup: SKILL.md must exist before delete");

            var result = plugin.DeleteSkillFiles(customBase);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(skillsDir, ToolName)).ShouldBeFalse(
                "custom path + relative SkillsPath Ã¢â€ â€™ delete from customBase/SKILLS");
        }

        [Fact]
        public void DeleteSkillFiles_NullPath_AbsoluteSkillsPath_DeletesFromAbsolutePath()
        {
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");

            var config = new ConnectionConfig { SkillsPath = absoluteSkillsDir, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            plugin.GenerateSkillFiles(); // create files at absoluteSkillsDir

            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue("setup: SKILL.md must exist before delete");

            var result = plugin.DeleteSkillFiles();

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(absoluteSkillsDir, ToolName)).ShouldBeFalse(
                "path=null + absolute SkillsPath Ã¢â€ â€™ delete from absoluteSkillsDir");
        }

        [Fact]
        public void DeleteSkillFiles_WithCustomPath_AbsoluteSkillsPath_IgnoresCustomPath()
        {
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");
            var customBase = Path.Combine(_tempDir, "would-be-wrong");

            var config = new ConnectionConfig { SkillsPath = absoluteSkillsDir, GenerateSkillFiles = false };
            using var plugin = BuildPlugin(config);

            plugin.GenerateSkillFiles(); // create files at absoluteSkillsDir

            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue("setup: SKILL.md must exist before delete");

            var result = plugin.DeleteSkillFiles(customBase);

            result.ShouldBeTrue();
            Directory.Exists(Path.Combine(absoluteSkillsDir, ToolName)).ShouldBeFalse(
                "absolute SkillsPath takes precedence Ã¢â‚¬â€ deletes from absoluteSkillsDir");
            Directory.Exists(customBase).ShouldBeFalse(
                "custom path must never be created when SkillsPath is absolute");
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ mock Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬

        private sealed class MockRunTool : IRunTool
        {
            public MockRunTool(string name) => Name = name;

            public string Name { get; }
            public string? Title => null;
            public string? Description => "Skill path test tool";
            public JsonNode? InputSchema => null;
            public JsonNode? OutputSchema => null;
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
