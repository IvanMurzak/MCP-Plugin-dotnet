/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Auth;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// <see cref="PluginCatalogCache"/>: what a reconnecting plugin's remembered lists are served to,
    /// for how long, and what drops them. Uses the real <see cref="AccountMcpStrategy"/> so tenant
    /// isolation is checked against the production routing rules.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginCatalogCacheTests
    {
        const string HashA = PluginReconnect.HashA;
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        const string PinA = "aabbccdd";
        const string PinB = "11223344";
        static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

        DateTimeOffset _now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        readonly AccountMcpStrategy _strategy = new AccountMcpStrategy();

        PluginCatalogCache NewCache(TimeSpan? grace = null, TimeSpan? wait = null,
            Func<string, CancellationToken, Task<ResponseListTool[]?>>? fetchTools = null)
            => new PluginCatalogCache(_strategy, grace ?? Grace, wait ?? TimeSpan.FromSeconds(1),
                clock: () => _now, fetchTools: fetchTools);

        // ───────────────────────────── grace ─────────────────────────────

        [Fact]
        public void DisconnectedPlugin_ListsAreServedWithinTheGrace()
        {
            var cache = NewCache();
            var tools = PluginReconnect.Tools("engine_tool");
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, tools);
            cache.Record("c1", PluginCatalogKind.Prompts, new ResponseListPrompts());

            Disconnect(cache, "c1");
            _now += Grace - TimeSpan.FromSeconds(1);

            using (PluginReconnect.Session("acc-1"))
            {
                cache.HasOfflineTargetForCurrentSession().ShouldBeTrue();
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeSameAs(tools);
                cache.GetOfflineList<ResponseListPrompts>(PluginCatalogKind.Prompts).ShouldNotBeNull();
            }
        }

        [Fact]
        public void DisconnectedPlugin_ListsExpireAfterTheGrace()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));

            Disconnect(cache, "c1");
            _now += Grace;

            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
            cache.OfflineCount.ShouldBe(0, "an expired entry must be pruned, not kept forever");
        }

        [Fact]
        public void ZeroGrace_RemembersNothing()
        {
            var cache = NewCache(grace: TimeSpan.Zero);
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));

            Disconnect(cache, "c1");

            cache.Enabled.ShouldBeFalse();
            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
        }

        [Fact]
        public void PluginThatNeverAnsweredAList_IsNotRemembered()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);

            Disconnect(cache, "c1");

            cache.OfflineCount.ShouldBe(0);
        }

        [Fact]
        public void ReconnectedPlugin_IsLiveAgain_AndALateCloseOfTheOldConnectionKeepsIt()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            Disconnect(cache, "c1");

            Connect(cache, "acc-1", "i1", "c2", HashA); // reconnect-replace by InstanceId
            cache.OnPluginDisconnected("c1");           // late close of the old connection: no-op

            cache.OfflineCount.ShouldBe(0);
            using (PluginReconnect.Session("acc-1"))
                cache.HasOfflineTargetForCurrentSession().ShouldBeFalse("the instance is live again");
        }

        // ───────────────────────────── isolation ─────────────────────────────

        [Fact]
        public void RememberedLists_AreNeverServedToAnotherAccount_OrToASessionWithoutIdentity()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            Disconnect(cache, "c1");

            using (PluginReconnect.Session("acc-2"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
            using (PluginReconnect.Session(null))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldNotBeNull("control: the owner still gets it");
        }

        [Fact]
        public void ProjectPin_SelectsOnlyTheMatchingRememberedInstance()
        {
            var cache = NewCache();
            var toolsA = PluginReconnect.Tools("tool_a");
            var toolsB = PluginReconnect.Tools("tool_b");
            Connect(cache, "acc-1", "iA", "cA", HashA, machine: "PC-A");
            Connect(cache, "acc-1", "iB", "cB", HashB, machine: "PC-B");
            cache.Record("cA", PluginCatalogKind.Tools, toolsA);
            cache.Record("cB", PluginCatalogKind.Tools, toolsB);
            Disconnect(cache, "cA");
            Disconnect(cache, "cB");

            using (PluginReconnect.Session("acc-1", pin: PinB))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeSameAs(toolsB);
            using (PluginReconnect.Session("acc-1", pin: PinA))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeSameAs(toolsA);
            using (PluginReconnect.Session("acc-1", pin: "deadbeef"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull("a pin never falls through to another project");
        }

        [Fact]
        public void StickySelection_SelectsTheChosenRememberedInstance()
        {
            var cache = NewCache();
            var toolsB = PluginReconnect.Tools("tool_b");
            Connect(cache, "acc-1", "iA", "cA", HashA, machine: "PC-A");
            Connect(cache, "acc-1", "iB", "cB", HashB, machine: "PC-B");
            cache.Record("cA", PluginCatalogKind.Tools, PluginReconnect.Tools("tool_a"));
            cache.Record("cB", PluginCatalogKind.Tools, toolsB);
            Disconnect(cache, "cA");
            Disconnect(cache, "cB");

            using (PluginReconnect.Session("acc-1", selected: "iB"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeSameAs(toolsB);
        }

        // ───────────────────────────── explicit "gone" ─────────────────────────────

        [Fact]
        public void EditorRestart_NewInstanceOfTheSameProject_DropsTheOldEntry()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i-old", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            Disconnect(cache, "c1");

            Connect(cache, "acc-1", "i-new", "c2", HashA); // same project/engine/machine, new InstanceId
            Disconnect(cache, "c2");                       // the new one never listed: not kept either

            cache.OfflineCount.ShouldBe(0);
            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
        }

        [Fact]
        public void ForgetAccount_DropsItsEntries_AndOnlyThose()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);
            Connect(cache, "acc-2", "i2", "c2", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            cache.Record("c2", PluginCatalogKind.Tools, PluginReconnect.Tools("t2"));
            Disconnect(cache, "c1");
            Disconnect(cache, "c2");

            cache.ForgetAccount("acc-1").ShouldBe(1);

            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();
            using (PluginReconnect.Session("acc-2"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldNotBeNull();
        }

        [Fact]
        public void InvalidatedList_IsNotServed()
        {
            var cache = NewCache();
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            cache.Record("c1", PluginCatalogKind.Prompts, new ResponseListPrompts());
            cache.Invalidate("c1", PluginCatalogKind.Prompts);
            Disconnect(cache, "c1");

            using (PluginReconnect.Session("acc-1"))
            {
                cache.GetOfflineList<ResponseListPrompts>(PluginCatalogKind.Prompts).ShouldBeNull();
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldNotBeNull();
            }
        }

        [Fact]
        public void UnregisteredConnection_CreatesNoEntry()
        {
            var cache = NewCache();
            cache.OnPluginConnected("not-registered"); // e.g. a rejected oauth plugin
            cache.Record("not-registered", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            cache.OnPluginDisconnected("not-registered");

            cache.OfflineCount.ShouldBe(0);
        }

        // ───────────────────────────── waiting for a reconnect ─────────────────────────────

        [Fact]
        public async Task WaitForReconnect_ReturnsTrueOnceThePluginIsLive()
        {
            var cache = NewCache(wait: TimeSpan.FromSeconds(10));
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            Disconnect(cache, "c1");
            var live = false;
            _ = Task.Delay(300).ContinueWith(_ => live = true);

            using (PluginReconnect.Session("acc-1"))
                (await cache.WaitForReconnectAsync(() => live, CancellationToken.None)).ShouldBe(true);
        }

        [Fact]
        public async Task WaitForReconnect_GivesUpAfterTheWait_AndAtOnceWithoutARememberedPlugin()
        {
            var cache = NewCache(wait: TimeSpan.FromMilliseconds(300));
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            Disconnect(cache, "c1");

            using (PluginReconnect.Session("acc-1"))
            {
                var started = DateTime.UtcNow;
                (await cache.WaitForReconnectAsync(() => false, CancellationToken.None)).ShouldBe(false);
                (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(5));
            }
            using (PluginReconnect.Session("acc-2"))
                (await cache.WaitForReconnectAsync(() => false, CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(200))).ShouldBeNull("nothing remembered: nothing to wait for");
        }

        [Fact]
        public async Task WaitForReconnect_EndsAtOnceWhenTheHostStops()
        {
            using var stopping = new CancellationTokenSource();
            var cache = new PluginCatalogCache(_strategy, Grace, TimeSpan.FromSeconds(30), clock: () => _now, stopping: stopping.Token);
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
            Disconnect(cache, "c1");

            using (PluginReconnect.Session("acc-1"))
            {
                var wait = cache.WaitForReconnectAsync(() => false, CancellationToken.None);
                stopping.Cancel();
                (await wait.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(false);
            }
        }

        // ───────────────────────────── tool changes ─────────────────────────────

        [Fact]
        public void SameToolSet_IgnoresOrder_ButNotContent()
        {
            PluginCatalogCache.SameToolSet(PluginReconnect.Tools("a", "b"), PluginReconnect.Tools("b", "a")).ShouldBeTrue();
            PluginCatalogCache.SameToolSet(PluginReconnect.Tools("a", "b"), PluginReconnect.Tools("a", "c")).ShouldBeFalse();
            PluginCatalogCache.SameToolSet(PluginReconnect.Tools("a"), PluginReconnect.Tools("a", "b")).ShouldBeFalse();

            var changed = PluginReconnect.Tools("a", "b");
            changed[0].Description = "different";
            PluginCatalogCache.SameToolSet(PluginReconnect.Tools("a", "b"), changed).ShouldBeFalse();
        }

        [Fact]
        public async Task ToolChange_AfterReconnectWithTheSameTools_IsNotForwarded()
        {
            var fetched = new TaskCompletionSource<bool>();
            var cache = NewCache(fetchTools: (_, _) => { fetched.TrySetResult(true); return Task.FromResult<ResponseListTool[]?>(PluginReconnect.Tools("a", "b")); });
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("b", "a"));
            Disconnect(cache, "c1");
            Connect(cache, "acc-1", "i1", "c2", HashA);
            var notified = 0;

            cache.TryNotifyIfToolsChanged("c2", () => Interlocked.Increment(ref notified)).ShouldBeTrue();
            await fetched.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);

            notified.ShouldBe(0);
        }

        [Fact]
        public async Task ToolChange_WithDifferentTools_IsForwardedOnce()
        {
            var cache = NewCache(fetchTools: (_, _) => Task.FromResult<ResponseListTool[]?>(PluginReconnect.Tools("a", "new")));
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("a"));
            var notified = new TaskCompletionSource<bool>();

            cache.TryNotifyIfToolsChanged("c1", () => notified.TrySetResult(true)).ShouldBeTrue();

            (await notified.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }

        [Fact]
        public async Task ToolChange_WhenThePluginDoesNotAnswer_IsForwarded()
        {
            var cache = NewCache(fetchTools: (_, _) => Task.FromException<ResponseListTool[]?>(new TimeoutException()));
            Connect(cache, "acc-1", "i1", "c1", HashA);
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("a"));
            var notified = new TaskCompletionSource<bool>();

            cache.TryNotifyIfToolsChanged("c1", () => notified.TrySetResult(true)).ShouldBeTrue();

            (await notified.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }

        [Fact]
        public void ToolChange_WithNoKnownTools_IsLeftToTheCaller()
        {
            var cache = NewCache(fetchTools: (_, _) => Task.FromResult<ResponseListTool[]?>(PluginReconnect.Tools("a")));
            Connect(cache, "acc-1", "i1", "c1", HashA);

            cache.TryNotifyIfToolsChanged("c1", () => { }).ShouldBeFalse();
        }

        // ───────────────────────────── single-plugin modes ─────────────────────────────

        [Fact]
        public void SinglePluginMode_RemembersTheOnePlugin_AndNotForAMultiPluginStrategy()
        {
            var connectionId = "catalog-single-" + Guid.NewGuid().ToString("N");
            var single = new Mock<IMcpConnectionStrategy>();
            single.Setup(s => s.AllowMultipleConnections).Returns(false);
            var multi = new Mock<IMcpConnectionStrategy>();
            multi.Setup(s => s.AllowMultipleConnections).Returns(true);
            var singleCache = new PluginCatalogCache(single.Object, Grace, TimeSpan.FromSeconds(1), clock: () => _now);
            var multiCache = new PluginCatalogCache(multi.Object, Grace, TimeSpan.FromSeconds(1), clock: () => _now);
            ClientUtils.AddClient(typeof(McpServerHub), connectionId, NullLogger.Instance);
            try
            {
                foreach (var cache in new[] { singleCache, multiCache })
                {
                    cache.OnPluginConnected(connectionId);
                    cache.Record(connectionId, PluginCatalogKind.Tools, PluginReconnect.Tools("t1"));
                    cache.OnPluginDisconnected(connectionId);
                }
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), connectionId, NullLogger.Instance);
            }

            singleCache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldNotBeNull();
            multiCache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull("no safe routing key without accounts");
        }

        // ───────────────────────────── helpers ─────────────────────────────

        void Connect(PluginCatalogCache cache, string account, string instanceId, string connectionId, string pathHash, string machine = "PC-1")
            => PluginReconnect.Register(_strategy, cache, account, instanceId, connectionId, pathHash, machine);

        void Disconnect(PluginCatalogCache cache, string connectionId)
            => PluginReconnect.Unregister(_strategy, cache, connectionId);
    }
}
