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
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tools;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// The two agent-actionable no-instance error variants of design 04 step 5 (mcp-authorize b4):
    /// pinned-no-match (project editor closed, other instances live) vs account-empty (nothing
    /// connected). Verifies both the text builder and the strategy resolution → text path.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class AgentActionableErrorsTests
    {
        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string HashB = "99887766554433221100ffeeddccbbaa99887766554433221100ffeeddccbbaa";
        const string PinB = "99887766";

        static PluginInstanceMetadata Meta(string instanceId, string engine = "unity", string project = "MyGame", string pathHash = HashA, string machine = "PC-1")
            => new PluginInstanceMetadata(instanceId, engine, project, pathHash, machine);

        static ConnectionIdentity Agent(string account) => new ConnectionIdentity(account, ConnectionIdentity.RoleAgent);

        static IDisposable SessionContext(ConnectionIdentity? identity, string? pin = null, string? selected = null)
        {
            McpSessionTokenContext.CurrentIdentity = identity;
            McpSessionTokenContext.CurrentProjectPin = pin;
            McpSessionTokenContext.CurrentSelectedInstanceId = selected;
            return new Reset();
        }

        sealed class Reset : IDisposable
        {
            public void Dispose()
            {
                McpSessionTokenContext.CurrentIdentity = null;
                McpSessionTokenContext.CurrentProjectPin = null;
                McpSessionTokenContext.CurrentSelectedInstanceId = null;
            }
        }

        // ─────────────────────────────── Text builder ───────────────────────────────

        [Fact]
        public void AccountEmpty_Text_NamesEnrollAndCli()
        {
            AgentActionableErrors.AccountEmpty.ShouldContain("enroll_engine_plugin");
            AgentActionableErrors.AccountEmpty.ShouldContain("unity-mcp-cli install-plugin --enroll");
            AgentActionableErrors.AccountEmpty.ShouldContain("No game engine is connected");
        }

        [Fact]
        public void PinnedNoMatch_Text_ListsOtherInstances_AndNeverSuggestsReinstall()
        {
            var registry = new AccountInstances();
            registry.Register("acc-1", Meta("i-godot", engine: "godot", project: "OtherGame", pathHash: HashB, machine: "PC-2"), "conn-2");

            var text = AgentActionableErrors.PinnedNoMatch(registry, "acc-1");

            text.ShouldContain("not connected");
            text.ShouldContain("Other connected instances:");
            text.ShouldContain("godot:OtherGame on PC-2");
            text.ShouldNotContain("install"); // never suggests re-installing
        }

        // ─────────────────────────── Strategy resolution → variant ───────────────────────────

        [Fact]
        public void ResolveCurrentSession_AccountEmpty_WhenNoInstances()
        {
            var strategy = new AccountMcpStrategy();
            using (SessionContext(Agent("acc-1")))
            {
                var resolution = strategy.ResolveCurrentSession();
                resolution.Kind.ShouldBe(InstanceResolutionKind.AccountEmpty);
                AgentActionableErrors.ForResolution(resolution, strategy.Instances, "acc-1")
                    .ShouldBe(AgentActionableErrors.AccountEmpty);
            }
        }

        [Fact]
        public void ResolveCurrentSession_PinnedNoMatch_WhenPinnedProjectEditorClosed()
        {
            var strategy = new AccountMcpStrategy();
            // Account has a live instance for a DIFFERENT project than the session's pin.
            strategy.Instances.Register("acc-1", Meta("i-other", engine: "unreal", project: "OtherGame", pathHash: HashB, machine: "PC-9"), "conn-9");

            using (SessionContext(Agent("acc-1"), pin: PinA))
            {
                var resolution = strategy.ResolveCurrentSession();
                resolution.Kind.ShouldBe(InstanceResolutionKind.NoMatchPinned);

                var text = AgentActionableErrors.ForResolution(resolution, strategy.Instances, "acc-1");
                text.ShouldContain("not connected");
                text.ShouldContain("unreal:OtherGame on PC-9");
            }
        }

        [Fact]
        public void ForResolution_Resolved_FallsToAccountEmptyText_ButIsNotUsedByCallers()
        {
            // Defensive: a Resolved kind is never passed to the error builder in practice; assert it
            // does not throw and yields the account-empty text as the harmless default.
            var strategy = new AccountMcpStrategy();
            var instance = strategy.Instances.Register("acc-1", Meta("i1"), "conn-1");
            var resolution = InstanceResolution.Resolved(instance);
            AgentActionableErrors.ForResolution(resolution, strategy.Instances, "acc-1")
                .ShouldBe(AgentActionableErrors.AccountEmpty);
        }
    }
}
