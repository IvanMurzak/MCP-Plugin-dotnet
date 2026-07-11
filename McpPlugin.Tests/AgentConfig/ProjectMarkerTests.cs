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
using com.IvanMurzak.McpPlugin.AgentConfig;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.AgentConfig.Tests
{
    /// <summary>
    /// Round-trip + precedence coverage for the committable project marker
    /// <c>&lt;project&gt;/.ai-game-dev/project.json</c>.
    /// </summary>
    public sealed class ProjectMarkerTests : IDisposable
    {
        private readonly string _projectRoot;

        public ProjectMarkerTests()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "agd-marker-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_projectRoot))
                Directory.Delete(_projectRoot, recursive: true);
        }

        [Fact]
        public void PathFor_IsUnderDotAiGameDev()
        {
            var path = ProjectMarker.PathFor(_projectRoot);
            path.ShouldBe(Path.Combine(_projectRoot, ".ai-game-dev", "project.json"));
        }

        [Fact]
        public void Read_ReturnsNull_WhenMarkerAbsent()
        {
            ProjectMarker.Read(_projectRoot).ShouldBeNull();
        }

        [Fact]
        public void Write_ThenRead_RoundTripsAllFields()
        {
            var marker = new ProjectMarker
            {
                ServerTarget = "https://ai-game.dev",
                PortOverride = 26123,
            };
            marker.Write(_projectRoot);

            File.Exists(ProjectMarker.PathFor(_projectRoot)).ShouldBeTrue();

            var read = ProjectMarker.Read(_projectRoot);
            read.ShouldNotBeNull();
            read!.ServerTarget.ShouldBe("https://ai-game.dev");
            read.PortOverride.ShouldBe(26123);
        }

        [Fact]
        public void Write_CreatesDotAiGameDevDirectory()
        {
            new ProjectMarker { ServerTarget = "https://ai-game.dev" }.Write(_projectRoot);
            Directory.Exists(Path.Combine(_projectRoot, ".ai-game-dev")).ShouldBeTrue();
        }

        [Fact]
        public void PortOverride_IsOmitted_WhenNull()
        {
            new ProjectMarker { ServerTarget = "https://ai-game.dev" }.Write(_projectRoot);

            var json = File.ReadAllText(ProjectMarker.PathFor(_projectRoot));
            json.ShouldNotContain("portOverride");

            var read = ProjectMarker.Read(_projectRoot);
            read!.PortOverride.ShouldBeNull();
        }

        [Fact]
        public void OverridePrecedence_MarkerPortWins_OverDerivedPort()
        {
            var projectPath = "/home/user/my-game";
            var derived = ProjectIdentity.Derive(projectPath); // no override
            derived.PortIsOverridden.ShouldBeFalse();

            new ProjectMarker { PortOverride = 27500 }.Write(_projectRoot);
            var marker = ProjectMarker.Read(_projectRoot);

            var resolved = ProjectIdentity.Derive(projectPath, marker);
            resolved.Port.ShouldBe(27500);
            resolved.Port.ShouldNotBe(derived.Port);
            resolved.PortIsOverridden.ShouldBeTrue();
            resolved.Pin.ShouldBe(derived.Pin); // pin still derived from the path, not the override
        }

        [Fact]
        public void Read_ToleratesEmptyFile()
        {
            var path = ProjectMarker.PathFor(_projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);

            var read = ProjectMarker.Read(_projectRoot);
            read.ShouldNotBeNull();
            read!.ServerTarget.ShouldBeNull();
            read.PortOverride.ShouldBeNull();
        }
    }
}
