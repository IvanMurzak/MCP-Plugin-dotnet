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
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Server.Strategy;
using com.IvanMurzak.McpPlugin.Server.Tests.Infrastructure;
using R3;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests.IsolationMatrix
{
    /// <summary>The Phase-2 gate's isolation promise for a plugin's expiry: two accounts, each hearing only its own.</summary>
    [Collection("McpPlugin.Server")]
    public sealed class PluginExpiryIsolationMatrixTests
    {
        const string HashA = PluginReconnect.HashA;
        const string HashB = "11223344aabbccdd556677889900aabbccddeeff00112233445566778899aabb";
        static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

        DateTimeOffset _now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        readonly AccountMcpStrategy _strategy = new AccountMcpStrategy();

        [Fact]
        public void TwoAccounts_EachExpiryReachesOnlyItsOwnAccount_AndNoSessionWithoutIdentity()
        {
            var cache = new PluginCatalogCache(_strategy, Grace, TimeSpan.FromSeconds(1), clock: () => _now);
            var first = new List<PluginCatalogExpiry>();
            var second = new List<PluginCatalogExpiry>();
            var noIdentity = new List<PluginCatalogExpiry>();
            cache.ExpiredFor("acc-1").Subscribe(first.Add);
            cache.ExpiredFor("acc-2").Subscribe(second.Add);
            cache.ExpiredFor(null).Subscribe(noIdentity.Add);
            cache.ExpiredFor(string.Empty).Subscribe(noIdentity.Add);

            RememberAndDrop(cache, "acc-1", "i1", "c1", HashA);
            _now += TimeSpan.FromMinutes(1);
            RememberAndDrop(cache, "acc-2", "i2", "c2", HashB);

            _now += Grace - TimeSpan.FromMinutes(1);
            cache.ExpireDue();
            first.ShouldHaveSingleItem().Account.ShouldBe("acc-1");
            second.ShouldBeEmpty();

            _now += TimeSpan.FromMinutes(1);
            cache.ExpireDue();
            first.Count.ShouldBe(1);
            second.ShouldHaveSingleItem().Account.ShouldBe("acc-2");
            noIdentity.ShouldBeEmpty();
        }

        void RememberAndDrop(PluginCatalogCache cache, string account, string instance, string connection, string hash)
        {
            PluginReconnect.Register(_strategy, cache, account, instance, connection, hash);
            cache.Record(connection, PluginCatalogKind.Tools, PluginReconnect.Tools("engine_tool"));
            PluginReconnect.Unregister(_strategy, cache, connection);
        }
    }
}
