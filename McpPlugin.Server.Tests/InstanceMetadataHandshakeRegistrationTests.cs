/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// mcp-authorize b7 handshake integration with the b3 account+instance pairing plane: the exact query
    /// keys an engine plugin sends in its hub handshake (<see cref="Consts.MCP.Server.HubQuery"/>) are read
    /// by the server, built into <see cref="PluginInstanceMetadata"/>, and land in the account registry —
    /// then resolve back to the instance by the session's project pin. This validates the client→server
    /// wire contract without needing a live SignalR hub (the client half asserts that
    /// <c>ConnectionInstanceMetadata.ToQuery()</c> emits these same keys).
    /// </summary>
    public sealed class InstanceMetadataHandshakeRegistrationTests
    {
        // A full 64-char SHA-256 hex project-path hash; its first 8 chars are the routing pin.
        const string ProjectPathHash = "abcd1234ef567890abcd1234ef567890abcd1234ef567890abcd1234ef567890";
        const string Pin = "abcd1234";
        const string AccountId = "account-1";
        const string ConnectionId = "conn-1";

        static IReadOnlyDictionary<string, string> ClientHandshakeQuery() => new Dictionary<string, string>
        {
            [Consts.MCP.Server.HubQuery.InstanceId] = "sess-42",
            [Consts.MCP.Server.HubQuery.Engine] = "unity",
            [Consts.MCP.Server.HubQuery.ProjectName] = "MyGame",
            [Consts.MCP.Server.HubQuery.ProjectPathHash] = ProjectPathHash,
            [Consts.MCP.Server.HubQuery.MachineName] = "DESKTOP-9",
        };

        [Fact]
        public void HandshakeQuery_RegistersInstance_IntoTheAccountRegistry()
        {
            var query = ClientHandshakeQuery();
            var strategy = new AccountMcpStrategy();
            var identity = new ConnectionIdentity(AccountId, ConnectionIdentity.RolePlugin);

            // The server reads the b7 query keys exactly as McpServerHub.TryRegisterOAuthInstanceAsync does.
            var metadata = AccountMcpStrategy.BuildInstanceMetadata(
                connectionId: ConnectionId,
                instanceId: query[Consts.MCP.Server.HubQuery.InstanceId],
                engine: query[Consts.MCP.Server.HubQuery.Engine],
                projectName: query[Consts.MCP.Server.HubQuery.ProjectName],
                projectPathHash: query[Consts.MCP.Server.HubQuery.ProjectPathHash],
                machineName: query[Consts.MCP.Server.HubQuery.MachineName]);

            var registered = strategy.RegisterInstance(identity, metadata, ConnectionId, NullLogger.Instance);

            registered.InstanceId.ShouldBe("sess-42");
            registered.Engine.ShouldBe("unity");
            registered.ProjectName.ShouldBe("MyGame");
            registered.ProjectPathHash.ShouldBe(ProjectPathHash);
            registered.MachineName.ShouldBe("DESKTOP-9");
            strategy.Instances.InstanceCount(AccountId).ShouldBe(1);
        }

        [Fact]
        public void RegisteredInstance_ResolvesBack_BySessionPin()
        {
            var query = ClientHandshakeQuery();
            var strategy = new AccountMcpStrategy();
            var identity = new ConnectionIdentity(AccountId, ConnectionIdentity.RolePlugin);

            var metadata = AccountMcpStrategy.BuildInstanceMetadata(
                connectionId: ConnectionId,
                instanceId: query[Consts.MCP.Server.HubQuery.InstanceId],
                engine: query[Consts.MCP.Server.HubQuery.Engine],
                projectName: query[Consts.MCP.Server.HubQuery.ProjectName],
                projectPathHash: query[Consts.MCP.Server.HubQuery.ProjectPathHash],
                machineName: query[Consts.MCP.Server.HubQuery.MachineName]);
            strategy.RegisterInstance(identity, metadata, ConnectionId, NullLogger.Instance);

            // The session pin (first 8 hex of the project-path hash) routes strictly to this instance.
            var resolution = strategy.Instances.Resolve(AccountId, Pin, selectedInstanceId: null);

            resolution.Instance.ShouldNotBeNull();
            resolution.Instance!.InstanceId.ShouldBe("sess-42");
            resolution.Instance.MatchesPin(Pin).ShouldBeTrue();

            // Fail closed: a pin for a DIFFERENT project never resolves to this account's instance.
            strategy.Instances.Resolve(AccountId, "ffffffff", selectedInstanceId: null)
                .Instance.ShouldBeNull();
        }
    }
}
