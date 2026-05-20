/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
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
        // Primary temp directory — cleaned up in Dispose.
        readonly string _tempDir;

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
        }

        // ── helpers ────────────────────────────────────────────────────────────────

        IMcpPlugin BuildPlugin(ConnectionConfig config)
            => new McpPluginBuilder(_version)
                .SetConfig(config)
                .AddTool(ToolName, new MockRunTool(ToolName))
                .Build(_reflector);

        // Returns the expected SKILL.md path given the resolved skills directory.
        static string SkillFile(string resolvedSkillsDir) =>
            Path.Combine(resolvedSkillsDir, ToolName, "SKILL.md");

        // ── Issue #107 — anchor resolution priority + loud-failure behaviour ──────

        [Fact]
        public void ResolveSkillsPath_AbsoluteSkillsPath_UsedAsIs()
        {
            // Absolute SkillsPath bypasses every anchor — basePath / ProjectRootPath are
            // ignored. Existing behaviour, asserted explicitly so the regression net is wide.
            var absoluteSkillsDir = Path.Combine(_tempDir, "abs-skills");
            var basePathThatMustBeIgnored = Path.Combine(_tempDir, "ignore-me-basepath");

            var config = new ConnectionConfig
            {
                SkillsPath = absoluteSkillsDir,
                GenerateSkillFiles = false,
                ProjectRootPath = Path.Combine(_tempDir, "ignore-me-projectroot")
            };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles(basePathThatMustBeIgnored);

            result.ShouldBeTrue();
            File.Exists(SkillFile(absoluteSkillsDir)).ShouldBeTrue(
                "absolute SkillsPath must resolve to itself, regardless of basePath / ProjectRootPath");
            Directory.Exists(basePathThatMustBeIgnored).ShouldBeFalse(
                "basePath must not be touched when SkillsPath is absolute");
            Directory.Exists(config.ProjectRootPath!).ShouldBeFalse(
                "ProjectRootPath must not be touched when SkillsPath is absolute");
        }

        [Fact]
        public void ResolveSkillsPath_RelativeWithExplicitBasePath_AnchorsOnBasePath()
        {
            // Relative SkillsPath + explicit basePath → files land at basePath/SkillsPath.
            // basePath wins over ProjectRootPath (priority is documented on ConnectionConfig).
            var customBase = Path.Combine(_tempDir, "base-from-arg");
            var projectRootThatMustBeIgnored = Path.Combine(_tempDir, "project-root-loses");
            const string relName = "SKILLS";

            var config = new ConnectionConfig
            {
                SkillsPath = relName,
                GenerateSkillFiles = false,
                ProjectRootPath = projectRootThatMustBeIgnored
            };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(Path.Combine(customBase, relName))).ShouldBeTrue(
                $"basePath + relative SkillsPath → basePath/{relName}");
            Directory.Exists(projectRootThatMustBeIgnored).ShouldBeFalse(
                "ProjectRootPath must not be touched when an explicit basePath was supplied");
        }

        [Fact]
        public void ResolveSkillsPath_RelativeWithProjectRootInConfig_AnchorsOnProjectRoot()
        {
            // Relative SkillsPath + no basePath + ProjectRootPath set → files land at
            // ProjectRootPath/SkillsPath. This is the new behaviour introduced by issue #107.
            var projectRoot = Path.Combine(_tempDir, "project-root");
            const string relName = "SKILLS";

            var config = new ConnectionConfig
            {
                SkillsPath = relName,
                GenerateSkillFiles = false,
                ProjectRootPath = projectRoot
            };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles();

            result.ShouldBeTrue();
            File.Exists(SkillFile(Path.Combine(projectRoot, relName))).ShouldBeTrue(
                $"null basePath + ProjectRootPath + relative SkillsPath → ProjectRootPath/{relName}");
        }

        [Fact]
        public void ResolveSkillsPath_RelativeBasePathOverridesProjectRoot()
        {
            // Priority check: explicit basePath beats ProjectRootPath even when both are set.
            var customBase = Path.Combine(_tempDir, "wins-base");
            var projectRoot = Path.Combine(_tempDir, "loses-projectroot");
            const string relName = "SKILLS";

            var config = new ConnectionConfig
            {
                SkillsPath = relName,
                GenerateSkillFiles = false,
                ProjectRootPath = projectRoot
            };
            using var plugin = BuildPlugin(config);

            var result = plugin.GenerateSkillFiles(customBase);

            result.ShouldBeTrue();
            File.Exists(SkillFile(Path.Combine(customBase, relName))).ShouldBeTrue(
                "basePath must win over ProjectRootPath when both are supplied");
            Directory.Exists(projectRoot).ShouldBeFalse(
                "ProjectRootPath must not be touched when basePath wins");
        }

        [Fact]
        public void ResolveSkillsPath_RelativeWithoutAnyAnchor_Throws()
        {
            // Loud-failure behaviour: no basePath + no ProjectRootPath + relative SkillsPath →
            // InvalidOperationException. This replaces the old silent CWD fallback.
            const string relName = "SKILLS";

            var config = new ConnectionConfig
            {
                SkillsPath = relName,
                GenerateSkillFiles = false,
                ProjectRootPath = null
            };
            using var plugin = BuildPlugin(config);

            var ex = Should.Throw<InvalidOperationException>(() => plugin.GenerateSkillFiles());
            ex.Message.ShouldContain(nameof(ConnectionConfig.ProjectRootPath));
            ex.Message.ShouldContain("SkillsPath");
        }

        [Fact]
        public void ConnectionConfig_ProjectRootPath_IsNotSerialized()
        {
            // Persistence-side regression guard: ProjectRootPath MUST be carried as runtime-only
            // state and MUST NOT round-trip to disk. Re-introducing it into the serialized form
            // would resurrect Unity-MCP #761's path-portability bug.
            var config = new ConnectionConfig
            {
                Host = "https://example.com",
                SkillsPath = "SKILLS",
                ProjectRootPath = "C:/Users/somebody/MyUnityProject"
            };

            var json = JsonSerializer.Serialize(config);
            var node = JsonNode.Parse(json)!.AsObject();

            node.ContainsKey(nameof(ConnectionConfig.ProjectRootPath)).ShouldBeFalse(
                $"{nameof(ConnectionConfig.ProjectRootPath)} must be marked [JsonIgnore] and never appear in serialized JSON");

            // Sanity-check: the other properties DID serialize, so [JsonIgnore] is targeted.
            node.ContainsKey(nameof(ConnectionConfig.Host)).ShouldBeTrue();
            node.ContainsKey(nameof(ConnectionConfig.SkillsPath)).ShouldBeTrue();
        }

        [Fact]
        public void OnToolsUpdated_WithMissingProjectRoot_LogsAndContinues()
        {
            // The two internal auto-fires (ctor + OnToolsUpdated subscription) MUST swallow the
            // new InvalidOperationException via try/catch + LogError instead of letting it bubble
            // into R3's fire-and-forget subscription context. Construction with a relative
            // SkillsPath and no ProjectRootPath must therefore NOT crash — it must log and
            // continue. This test asserts the no-crash contract; the matching LogError is
            // observable in the xunit test output via the XunitTestOutputLoggerProvider.
            var config = new ConnectionConfig
            {
                SkillsPath = "SKILLS",
                GenerateSkillFiles = true,
                ProjectRootPath = null
            };

            Should.NotThrow(() =>
            {
                using var plugin = BuildPlugin(config);
                // Construction triggers the initial GenerateSkillFilesIfNeeded() call.
                // If the wrapper is missing, the InvalidOperationException would escape
                // synchronously and fail the test.
            });
        }

        // ── mock ───────────────────────────────────────────────────────────────────

        private sealed class MockRunTool : IRunTool
        {
            public MockRunTool(string name) => Name = name;

            public string Name { get; }
            public string? Title => null;
            public string? Description => "Skill path test tool";
            public string? SkillDescription => null;
            public string? SkillBody => null;
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
