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
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// <see cref="PluginCatalogCache.ExpiredFor"/>: fires once when a plugin stays gone past the grace,
    /// with no client traffic, and never for an entry that was dropped for another reason.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginCatalogExpiryTests
    {
        const string HashA = PluginReconnect.HashA;
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

        DateTimeOffset _now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        readonly AccountMcpStrategy _strategy = new AccountMcpStrategy();
        readonly List<PluginCatalogExpiry> _fired = new List<PluginCatalogExpiry>();

        PluginCatalogCache NewCache(TimeSpan? grace = null, CancellationToken stopping = default, bool realClock = false)
        {
            var cache = new PluginCatalogCache(_strategy, grace ?? Grace, TimeSpan.FromSeconds(1),
                clock: realClock ? null : () => _now, stopping: stopping);
            cache.ExpiredFor("acc-1").Subscribe(expiry => { lock (_fired) _fired.Add(expiry); });
            return cache;
        }

        void RememberAndDrop(PluginCatalogCache cache, string account, string instance, string connection, string hash = HashA)
        {
            PluginReconnect.Register(_strategy, cache, account, instance, connection, hash);
            cache.Record(connection, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(_strategy, cache, connection);
        }

        int FiredCount { get { lock (_fired) return _fired.Count; } }

        void NothingFiresLongAfterTheGrace(PluginCatalogCache cache)
        {
            _now += Grace + Grace;
            cache.ExpireDue();
            _fired.ShouldBeEmpty();
        }

        static object? ExpiryTimer(PluginCatalogCache cache)
            => typeof(PluginCatalogCache).GetField("_expiryTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache);

        // ───────────────────────────── when it fires ─────────────────────────────

        [Fact]
        public async Task PluginStaysGone_TheTimerPublishesTheExpiry_WithNoReads()
        {
            var cache = NewCache(grace: TimeSpan.FromMilliseconds(100), realClock: true);
            PluginReconnect.Register(_strategy, cache, "acc-1", "i1", "c1");
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            cache.Record("c1", PluginCatalogKind.Prompts, new ResponseListPrompts());
            PluginReconnect.Unregister(_strategy, cache, "c1");

            for (var i = 0; i < 100 && FiredCount == 0; i++)
                await Task.Delay(50);

            FiredCount.ShouldBe(1);
            _fired[0].Account.ShouldBe("acc-1");
            _fired[0].Kinds.ShouldBe(new[] { PluginCatalogKind.Tools, PluginCatalogKind.Prompts });
            cache.OfflineCount.ShouldBe(0);
        }

        [Fact]
        public void ExpireDue_FiresOnlyWhenTheGraceEnded_AndOnlyOnce()
        {
            var cache = NewCache();
            RememberAndDrop(cache, "acc-1", "i1", "c1");

            _now += Grace - TimeSpan.FromSeconds(1);
            cache.ExpireDue();
            _fired.ShouldBeEmpty();

            _now += TimeSpan.FromSeconds(1);
            cache.ExpireDue();
            cache.ExpireDue();
            _fired.Count.ShouldBe(1);
        }

        [Fact]
        public void AReadOfAnExpiredEntry_GetsNothing_AndItStillFiresOnce()
        {
            var cache = NewCache();
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            _now += Grace;

            using (PluginReconnect.Session("acc-1"))
                cache.GetOfflineList<ResponseListTool[]>(PluginCatalogKind.Tools).ShouldBeNull();

            cache.ExpireDue();
            _fired.Count.ShouldBe(1);
        }

        // ───────────────────────────── when it must not fire ─────────────────────────────

        [Fact]
        public void PluginReturnedWithinTheGrace_NothingFires()
        {
            var cache = NewCache();
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            PluginReconnect.Register(_strategy, cache, "acc-1", "i1", "c2");

            NothingFiresLongAfterTheGrace(cache);
        }

        [Fact]
        public void EntryReplacedByANewInstanceOfTheSameProject_NothingFires()
        {
            var cache = NewCache();
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            PluginReconnect.Register(_strategy, cache, "acc-1", "i2", "c2"); // same project/engine/machine

            NothingFiresLongAfterTheGrace(cache);
        }

        [Fact]
        public void ForgottenAccount_NothingFires()
        {
            var cache = NewCache();
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            cache.ForgetAccount("acc-1");

            NothingFiresLongAfterTheGrace(cache);
        }

        [Fact]
        public void PluginThatRememberedNothing_NothingFires()
        {
            var cache = NewCache();
            PluginReconnect.Register(_strategy, cache, "acc-1", "i1", "c1");
            PluginReconnect.Unregister(_strategy, cache, "c1");

            NothingFiresLongAfterTheGrace(cache);
        }

        // ───────────────────────────── isolation ─────────────────────────────

        [Fact]
        public void TwoAccounts_EachExpiryReachesOnlyItsOwnAccount()
        {
            var cache = NewCache();
            var second = new List<PluginCatalogExpiry>();
            var noIdentity = new List<PluginCatalogExpiry>();
            cache.ExpiredFor("acc-2").Subscribe(second.Add);
            cache.ExpiredFor(null).Subscribe(noIdentity.Add);
            cache.ExpiredFor(string.Empty).Subscribe(noIdentity.Add);
            RememberAndDrop(cache, "acc-1", "i1", "c1", HashA);
            _now += TimeSpan.FromMinutes(1);
            RememberAndDrop(cache, "acc-2", "i2", "c2", HashB);

            _now += Grace - TimeSpan.FromMinutes(1);
            cache.ExpireDue();

            _fired.ShouldHaveSingleItem().Account.ShouldBe("acc-1");
            second.ShouldBeEmpty();

            _now += TimeSpan.FromMinutes(1);
            cache.ExpireDue();
            _fired.Count.ShouldBe(1);
            second.ShouldHaveSingleItem().Account.ShouldBe("acc-2");
            noIdentity.ShouldBeEmpty("a session with no identity gets nothing");
        }

        [Fact]
        public void SinglePluginMode_ExpiryReachesEverySession_WhateverItsToken()
        {
            var connection = "expiry-single-" + Guid.NewGuid().ToString("N");
            var single = new Mock<IMcpConnectionStrategy>();
            single.Setup(s => s.AllowMultipleConnections).Returns(false);
            var cache = new PluginCatalogCache(single.Object, Grace, TimeSpan.Zero, clock: () => _now);
            var heard = new List<string?>();
            foreach (var session in new[] { null, "token-1", "token-2" })
                cache.ExpiredFor(session).Subscribe(_ => heard.Add(session));
            ClientUtils.AddClient(typeof(McpServerHub), connection, NullLogger.Instance);
            try
            {
                cache.OnPluginConnected(connection);
                cache.Record(connection, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
                cache.OnPluginDisconnected(connection);
            }
            finally
            {
                ClientUtils.RemoveClient(typeof(McpServerHub), connection, NullLogger.Instance);
            }

            _now += Grace;
            cache.ExpireDue();

            heard.ShouldBe(new[] { null, "token-1", "token-2" });
        }

        [Fact]
        public void ACustomMultiPluginStrategy_HasNoRoutingKey_SoNothingReachesASession()
        {
            var multi = new Mock<IMcpConnectionStrategy>();
            multi.Setup(s => s.AllowMultipleConnections).Returns(true);
            var cache = new PluginCatalogCache(multi.Object, Grace, TimeSpan.Zero, clock: () => _now);
            var heard = 0;
            cache.ExpiredFor(null).Subscribe(_ => heard++);

            cache.OnPluginConnected("c1");
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            cache.OnPluginDisconnected("c1");
            _now += Grace;
            cache.ExpireDue();

            heard.ShouldBe(0);
        }

        // ───────────────────────────── a later return ─────────────────────────────

        [Fact]
        public void PluginReturnsAfterTheExpiry_ItsToolsAreForwarded_EvenIfUnchanged()
        {
            var fetched = PluginReconnect.Tools("engine_tool");
            var cache = new PluginCatalogCache(_strategy, Grace, TimeSpan.FromSeconds(1), clock: () => _now,
                fetchTools: (_, _) => Task.FromResult<ResponseListTool[]?>(fetched));
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            _now += Grace;
            cache.ExpireDue();

            PluginReconnect.Register(_strategy, cache, "acc-1", "i1", "c2");

            cache.TryNotifyIfToolsChanged("c2", () => { }).ShouldBeFalse(
                "agents were told the tools left, so the caller must tell them they are back");
        }

        // ───────────────────────────── timer lifecycle ─────────────────────────────

        [Fact]
        public void ZeroGrace_NeverCreatesATimer_AndNothingFires()
        {
            var cache = NewCache(grace: TimeSpan.Zero);
            RememberAndDrop(cache, "acc-1", "i1", "c1");

            ExpiryTimer(cache).ShouldBeNull();
            _now += Grace;
            cache.ExpireDue();
            _fired.ShouldBeEmpty();
        }

        [Fact]
        public void ZeroGrace_DoesNotAddATimerAtHostStop()
        {
            using var stopping = new CancellationTokenSource();
            var cache = NewCache(grace: TimeSpan.Zero, stopping: stopping.Token);

            stopping.Cancel();

            ExpiryTimer(cache).ShouldBeNull();
        }

        [Fact]
        public void NoOfflinePlugin_NoTimer_UntilOneDisconnects()
        {
            var cache = NewCache();
            PluginReconnect.Register(_strategy, cache, "acc-1", "i1", "c1");
            cache.Record("c1", PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            ExpiryTimer(cache).ShouldBeNull();

            PluginReconnect.Unregister(_strategy, cache, "c1");

            ExpiryTimer(cache).ShouldNotBeNull();
        }

        [Fact]
        public async Task HostStopping_StopsTheTimer()
        {
            using var stopping = new CancellationTokenSource();
            var cache = NewCache(grace: TimeSpan.FromMilliseconds(300), stopping: stopping.Token, realClock: true);
            RememberAndDrop(cache, "acc-1", "i1", "c1");
            ExpiryTimer(cache).ShouldNotBeNull();

            stopping.Cancel();
            ExpiryTimer(cache).ShouldBeNull();

            await Task.Delay(600);
            FiredCount.ShouldBe(0);
            cache.OfflineCount.ShouldBe(1);

            RememberAndDrop(cache, "acc-1", "i1", "c2");
            ExpiryTimer(cache).ShouldBeNull("a plugin dropping during shutdown must not start it again");
        }
    }
}
