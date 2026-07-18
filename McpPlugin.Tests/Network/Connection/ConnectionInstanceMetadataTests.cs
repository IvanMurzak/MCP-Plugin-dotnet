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
using com.IvanMurzak.McpPlugin.AgentConfig;
using com.IvanMurzak.McpPlugin.Common;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Tests.Network.Connection
{
    /// <summary>
    /// Coverage for the mcp-authorize b7 instance-metadata handshake payload the plugin sends on its hub
    /// connection (design 04/06): correct query-key mapping, URL append semantics, and pin/hash consistency.
    /// </summary>
    public sealed class ConnectionInstanceMetadataTests
    {
        const string ProjectRoot = "/home/dev/MyGame";

        [Fact]
        public void Create_DerivesDualHash_V2Primary_V1Legacy_WithPinsAsPrefix()
        {
            var metadata = ConnectionInstanceMetadata.Create("unity", "MyGame", ProjectRoot);

            // Primary hash is the v2 (separator-normalized) hash; the v2 pin is its prefix.
            metadata.ProjectPathHash.ShouldBe(ProjectIdentity.DeriveProjectPathHashV2(ProjectRoot));
            metadata.ProjectPathHash.Length.ShouldBe(64);
            metadata.ProjectPathHash.ShouldStartWith(ProjectIdentity.DerivePinV2(ProjectRoot));

            // Legacy hash is the v1 hash of the same path; the v1 pin is its prefix.
            metadata.ProjectPathHashLegacy.ShouldBe(ProjectIdentity.DeriveProjectPathHash(ProjectRoot));
            metadata.ProjectPathHashLegacy.Length.ShouldBe(64);
            metadata.ProjectPathHashLegacy.ShouldStartWith(ProjectIdentity.DerivePin(ProjectRoot));

            metadata.InstanceId.ShouldNotBeNullOrEmpty();
            metadata.Engine.ShouldBe("unity");
            metadata.ProjectName.ShouldBe("MyGame");
        }

        [Fact]
        public void Create_OnWindowsBackslashRoot_V2AndLegacyHashesDiffer()
        {
            var backslashRoot = "C:" + ((char)92) + "Games" + ((char)92) + "MyGame";
            var metadata = ConnectionInstanceMetadata.Create("unity", "MyGame", backslashRoot);

            // v2 normalizes '\' -> '/', so the primary hash differs from the v1 legacy hash on Windows —
            // this is exactly the divergence (B5) the dual-hash transition carries both sides of.
            metadata.ProjectPathHash.ShouldBe(ProjectIdentity.DeriveProjectPathHashV2(backslashRoot));
            metadata.ProjectPathHashLegacy.ShouldBe(ProjectIdentity.DeriveProjectPathHash(backslashRoot));
            metadata.ProjectPathHash.ShouldNotBe(metadata.ProjectPathHashLegacy);
        }

        [Fact]
        public void ToQuery_CarriesBothV2AndLegacyHashes()
        {
            var metadata = ConnectionInstanceMetadata.Create("unity", "MyGame", ProjectRoot);

            var query = metadata.ToQuery();

            query[Consts.MCP.Server.HubQuery.ProjectPathHash].ShouldBe(metadata.ProjectPathHash);
            query[Consts.MCP.Server.HubQuery.ProjectPathHashLegacy].ShouldBe(metadata.ProjectPathHashLegacy);
        }

        [Fact]
        public void Create_DefaultsInstanceIdAndMachineName()
        {
            var metadata = ConnectionInstanceMetadata.Create("godot", "Proj", ProjectRoot);

            Guid.TryParse(metadata.InstanceId, out _).ShouldBeTrue("a default InstanceId should be a GUID");
            metadata.MachineName.ShouldBe(Environment.MachineName);
        }

        [Fact]
        public void Create_HonoursExplicitInstanceIdAndMachineName()
        {
            var metadata = ConnectionInstanceMetadata.Create("unreal", "P", ProjectRoot, instanceId: "sess-1", machineName: "BUILDBOX");

            metadata.InstanceId.ShouldBe("sess-1");
            metadata.MachineName.ShouldBe("BUILDBOX");
        }

        [Fact]
        public void ToQuery_UsesTheHubQueryKeys()
        {
            var metadata = new ConnectionInstanceMetadata("sess-1", "unity", "MyGame", "abcd1234ef", "DESKTOP-1");

            var query = metadata.ToQuery();

            query[Consts.MCP.Server.HubQuery.InstanceId].ShouldBe("sess-1");
            query[Consts.MCP.Server.HubQuery.Engine].ShouldBe("unity");
            query[Consts.MCP.Server.HubQuery.ProjectName].ShouldBe("MyGame");
            query[Consts.MCP.Server.HubQuery.ProjectPathHash].ShouldBe("abcd1234ef");
            query[Consts.MCP.Server.HubQuery.MachineName].ShouldBe("DESKTOP-1");
        }

        [Fact]
        public void ToQuery_OmitsEmptyFields_ButAlwaysKeepsInstanceId()
        {
            var metadata = new ConnectionInstanceMetadata("sess-1", engine: "", projectName: "", projectPathHash: "", machineName: "");

            var query = metadata.ToQuery();

            query.Count.ShouldBe(1);
            query[Consts.MCP.Server.HubQuery.InstanceId].ShouldBe("sess-1");
            query.ContainsKey(Consts.MCP.Server.HubQuery.Engine).ShouldBeFalse();
        }

        [Fact]
        public void AppendToUrl_AddsQueryString_AndEncodes()
        {
            var metadata = new ConnectionInstanceMetadata("sess 1", "unity", "My Game", "abcd", "DESK");

            var url = metadata.AppendToUrl("http://localhost:8080/hub/mcp-server");

            url.ShouldStartWith("http://localhost:8080/hub/mcp-server?");
            url.ShouldContain($"{Consts.MCP.Server.HubQuery.InstanceId}=sess%201");   // space URL-encoded
            url.ShouldContain($"{Consts.MCP.Server.HubQuery.ProjectName}=My%20Game");
            // The token must never appear in the query — this payload carries only non-secret identity.
            url.ShouldNotContain("Bearer");
            url.ShouldNotContain("access_token");
        }

        [Fact]
        public void AppendToUrl_PreservesExistingQuery_WithAmpersand()
        {
            var metadata = new ConnectionInstanceMetadata("sess-1", "unity", "", "", "");

            var url = metadata.AppendToUrl("http://localhost:8080/hub?existing=1");

            url.ShouldStartWith("http://localhost:8080/hub?existing=1&");
            url.ShouldContain($"{Consts.MCP.Server.HubQuery.InstanceId}=sess-1");
        }
    }
}
