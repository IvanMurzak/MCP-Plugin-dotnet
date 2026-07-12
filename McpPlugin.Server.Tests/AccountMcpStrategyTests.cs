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
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Strategy-level tests for the oauth account+instance pairing plane (mcp-authorize b3): live
    /// routing via <see cref="McpSessionTokenContext"/>, account-scoped notification routing (the
    /// issue-#102 cross-tenant regression re-keyed by account), and account-scoped session data.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class AccountMcpStrategyTests
    {
        readonly ILogger _logger = new Mock<ILogger>().Object;

        const string HashA = "aabbccdd11223344556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";

        static PluginInstanceMetadata Meta(string instanceId, string engine = "unity", string project = "MyGame", string pathHash = HashA, string machine = "PC-1")
            => new PluginInstanceMetadata(instanceId, engine, project, pathHash, machine);

        static ConnectionIdentity Plugin(string account) => new ConnectionIdentity(account, ConnectionIdentity.RolePlugin);
        static ConnectionIdentity Agent(string account) => new ConnectionIdentity(account, ConnectionIdentity.RoleAgent);

        AccountMcpStrategy NewStrategy() => new AccountMcpStrategy();

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

        [Fact]
        public void AuthOption_IsOAuth() => NewStrategy().AuthOption.ShouldBe(Consts.MCP.Server.AuthOption.oauth);

        // ─────────────────────────── Live routing (ResolveConnectionId) ───────────────────────────

        [Fact]
        public void ResolveConnectionId_RoutesToAccountsInstance()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            using (SessionContext(Agent("acc-1")))
            {
                strategy.ResolveConnectionId(token: "ignored", retryOffset: 0).ShouldBe("plugin-conn");
            }
        }

        [Fact]
        public void ResolveConnectionId_NoIdentity_FailsClosed()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            using (SessionContext(identity: null))
            {
                strategy.ResolveConnectionId("some-token", 0).ShouldBeNull();
            }
        }

        [Fact]
        public void ResolveConnectionId_ForeignAccount_FailsClosed()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            // An agent of a DIFFERENT account must never reach acc-1's instance.
            using (SessionContext(Agent("acc-2")))
            {
                strategy.ResolveConnectionId("ignored", 0).ShouldBeNull();
            }
        }

        [Fact]
        public void ResolveConnectionId_PinNoMatch_FailsClosed()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1", pathHash: HashA), "plugin-conn", _logger);

            using (SessionContext(Agent("acc-1"), pin: "deadbeef"))
            {
                strategy.ResolveConnectionId("ignored", 0).ShouldBeNull(); // pin never falls through
            }
        }

        [Fact]
        public void ResolveConnectionId_PinMatch_Routes()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1", pathHash: HashA), "plugin-conn", _logger);

            using (SessionContext(Agent("acc-1"), pin: PinA))
            {
                strategy.ResolveConnectionId("ignored", 0).ShouldBe("plugin-conn");
            }
        }

        // ─────────────────────────── OnPluginDisconnected ───────────────────────────

        [Fact]
        public void OnPluginDisconnected_RemovesInstance()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);
            strategy.Instances.InstanceCount("acc-1").ShouldBe(1);

            strategy.OnPluginDisconnected(typeof(McpServerHub), "plugin-conn", _logger);

            strategy.Instances.InstanceCount("acc-1").ShouldBe(0);
        }

        // ─────────────────────────── Notification scoping (ShouldNotifySession) ───────────────────────────

        [Fact]
        public void ShouldNotifySession_SameAccount_True()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            strategy.ShouldNotifySession("plugin-conn", sessionId: "acc-1").ShouldBeTrue();
        }

        [Fact]
        public void ShouldNotifySession_DifferentAccount_False()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            strategy.ShouldNotifySession("plugin-conn", sessionId: "acc-2").ShouldBeFalse();
        }

        [Fact]
        public void ShouldNotifySession_UnknownPluginConnection_False()
        {
            var strategy = NewStrategy();
            strategy.ShouldNotifySession("unknown-conn", sessionId: "acc-1").ShouldBeFalse();
        }

        // ─────────────────────────── ResolveNotificationTarget ───────────────────────────

        [Fact]
        public void ResolveNotificationTarget_TargetsOnlyThatAccountsInstances()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1", machine: "PC-1"), "conn-1a", _logger);
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i2", machine: "PC-2"), "conn-1b", _logger);
            strategy.RegisterInstance(Plugin("acc-2"), Meta("i3"), "conn-2", _logger);

            var target = strategy.ResolveNotificationTarget("acc-1");

            target.Kind.ShouldBe(NotificationTarget.TargetKind.SpecificMany);
            target.ConnectionIds.ShouldBe(new[] { "conn-1a", "conn-1b" }, ignoreOrder: true);
            target.ConnectionIds.ShouldNotContain("conn-2"); // never another tenant's plugin
        }

        [Fact]
        public void ResolveNotificationTarget_SingleInstance_CollapsesToSpecific()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "conn-1", _logger);

            var target = strategy.ResolveNotificationTarget("acc-1");

            target.Kind.ShouldBe(NotificationTarget.TargetKind.Specific);
            target.ConnectionId.ShouldBe("conn-1");
        }

        [Fact]
        public void ResolveNotificationTarget_UnknownAccount_Drops_NeverBroadcasts()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "conn-1", _logger);

            var target = strategy.ResolveNotificationTarget("acc-empty");

            target.Kind.ShouldBe(NotificationTarget.TargetKind.Drop);
        }

        [Fact]
        public void ResolveNotificationTarget_NullRoutingToken_Drops()
        {
            NewStrategy().ResolveNotificationTarget(null).Kind.ShouldBe(NotificationTarget.TargetKind.Drop);
        }

        [Fact]
        public void ResolveNotificationTarget_NeverBroadcasts()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "conn-1", _logger);
            foreach (var probe in new[] { (string?)null, "", "acc-1", "acc-unknown" })
                strategy.ResolveNotificationTarget(probe).Kind.ShouldNotBe(NotificationTarget.TargetKind.Broadcast);
        }

        // ─────────────────── Cross-tenant notification regression (issue #102, re-keyed) ───────────────────

        sealed class HubMocks
        {
            public Mock<IHubContext<McpServerHub, IClientMcpRpc>> HubContext { get; } = new();
            public Mock<IHubClients<IClientMcpRpc>> HubClients { get; } = new();
            public Mock<IClientMcpRpc> AllClients { get; } = new();

            public HubMocks()
            {
                HubClients.Setup(c => c.All).Returns(AllClients.Object);
                HubContext.Setup(c => c.Clients).Returns(HubClients.Object);
            }

            public Mock<IClientMcpRpc> RegisterClient(string connectionId)
            {
                var mock = new Mock<IClientMcpRpc>();
                HubClients.Setup(c => c.Client(connectionId)).Returns(mock.Object);
                return mock;
            }
        }

        // Mirrors McpServerService.NotifyClientConnectedAsync's dispatch shape for the account plane.
        static async Task SimulateNotifyClientConnected(IMcpConnectionStrategy strategy, string? routingToken,
            IHubContext<McpServerHub, IClientMcpRpc> hubContext, McpClientData connected)
        {
            var target = strategy.ResolveNotificationTarget(routingToken);
            switch (target.Kind)
            {
                case NotificationTarget.TargetKind.Specific:
                    await hubContext.Clients.Client(target.ConnectionId!).OnMcpClientConnected(connected, new[] { connected });
                    break;
                case NotificationTarget.TargetKind.SpecificMany:
                    foreach (var cid in target.ConnectionIds)
                        await hubContext.Clients.Client(cid).OnMcpClientConnected(connected, new[] { connected });
                    break;
                case NotificationTarget.TargetKind.Broadcast:
                    await hubContext.Clients.All.OnMcpClientConnected(connected, new[] { connected });
                    break;
                case NotificationTarget.TargetKind.Drop:
                    break;
            }
        }

        [Fact]
        public async Task Notify_TwoAccounts_OnlyOwnAccountsPluginReceives()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-A"), Meta("iA"), "conn-A", _logger);
            strategy.RegisterInstance(Plugin("acc-B"), Meta("iB"), "conn-B", _logger);

            var hub = new HubMocks();
            var pluginA = hub.RegisterClient("conn-A");
            var pluginB = hub.RegisterClient("conn-B");
            var clientData = new McpClientData { IsConnected = true, ClientName = "Claude" };

            // An agent session of account A connects.
            await SimulateNotifyClientConnected(strategy, routingToken: "acc-A", hub.HubContext.Object, clientData);

            pluginA.Verify(c => c.OnMcpClientConnected(clientData, It.IsAny<McpClientData[]>()), Times.Once);
            pluginB.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
        }

        [Fact]
        public async Task Notify_ForeignSessionWithNoInstance_Drops_DoesNotBroadcast()
        {
            // Regression for issue #102, re-keyed by account: a session whose account has NO live
            // instance must be DROPPED — never broadcast into an unrelated tenant's plugin.
            var strategy = NewStrategy();
            var unrelated = strategy; // acc-other has a plugin; the foreign session's account has none
            unrelated.RegisterInstance(Plugin("acc-other"), Meta("iO"), "conn-other", _logger);

            var hub = new HubMocks();
            var otherPlugin = hub.RegisterClient("conn-other");
            var foreignClient = new McpClientData { IsConnected = true, ClientName = "Foreign" };

            await SimulateNotifyClientConnected(strategy, routingToken: "acc-foreign", hub.HubContext.Object, foreignClient);

            otherPlugin.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
            hub.AllClients.Verify(c => c.OnMcpClientConnected(It.IsAny<McpClientData>(), It.IsAny<McpClientData[]>()), Times.Never);
        }

        // ─────────────────────────── Account-scoped session data ───────────────────────────

        [Fact]
        public void GetAllClientData_ScopesByAccount()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            var tracker = new Mock<IMcpSessionTracker>();
            var expected = new System.Collections.Generic.List<McpClientData> { new McpClientData { IsConnected = true } };
            tracker.Setup(t => t.GetAllClientData("acc-1")).Returns(expected);

            var result = strategy.GetAllClientData("plugin-conn", tracker.Object);

            result.Length.ShouldBe(1);
            tracker.Verify(t => t.GetAllClientData("acc-1"), Times.Once);
        }

        [Fact]
        public void GetClientData_UnknownConnection_ReturnsEmpty_DeniesUnscoped()
        {
            var strategy = NewStrategy();
            var tracker = new Mock<IMcpSessionTracker>();

            var result = strategy.GetClientData("unknown-conn", tracker.Object);

            result.IsConnected.ShouldBeFalse();
            tracker.Verify(t => t.GetClientDataByToken(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void GetServerData_ScopesByAccount()
        {
            var strategy = NewStrategy();
            strategy.RegisterInstance(Plugin("acc-1"), Meta("i1"), "plugin-conn", _logger);

            var tracker = new Mock<IMcpSessionTracker>();
            var expected = new McpServerData { IsAiAgentConnected = true };
            tracker.Setup(t => t.GetServerDataByToken("acc-1")).Returns(expected);

            var result = strategy.GetServerData("plugin-conn", tracker.Object);

            result.ShouldBeSameAs(expected);
            tracker.Verify(t => t.GetServerDataByToken("acc-1"), Times.Once);
        }

        // ─────────────────────────── BuildInstanceMetadata ───────────────────────────

        [Fact]
        public void BuildInstanceMetadata_UsesConnectionIdWhenInstanceIdAbsent()
        {
            var meta = AccountMcpStrategy.BuildInstanceMetadata("conn-x", instanceId: null,
                engine: "godot", projectName: "P", projectPathHash: "abc", machineName: "M");
            meta.InstanceId.ShouldBe("conn-x");
            meta.Engine.ShouldBe("godot");
        }

        [Fact]
        public void BuildInstanceMetadata_UsesProvidedInstanceId()
        {
            var meta = AccountMcpStrategy.BuildInstanceMetadata("conn-x", instanceId: "real-id",
                engine: null, projectName: null, projectPathHash: null, machineName: null);
            meta.InstanceId.ShouldBe("real-id");
            meta.Engine.ShouldBe(string.Empty);
        }
    }
}
