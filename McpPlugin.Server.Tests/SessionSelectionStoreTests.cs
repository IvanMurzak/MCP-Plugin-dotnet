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
using com.IvanMurzak.McpPlugin.Server.Tools;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Unit tests for the per-MCP-session sticky-selection store (mcp-authorize b4, design 04 step 2):
    /// set/replace/get/clear semantics and per-session isolation.
    /// </summary>
    public class SessionSelectionStoreTests
    {
        [Fact]
        public void Set_Then_Get_ReturnsValue()
        {
            var store = new SessionSelectionStore();
            store.Set("session-1", "inst-A");
            store.Get("session-1").ShouldBe("inst-A");
        }

        [Fact]
        public void Set_Replaces_PriorSelection()
        {
            var store = new SessionSelectionStore();
            store.Set("session-1", "inst-A");
            store.Set("session-1", "inst-B");
            store.Get("session-1").ShouldBe("inst-B");
        }

        [Fact]
        public void Get_UnknownSession_ReturnsNull()
        {
            var store = new SessionSelectionStore();
            store.Get("nope").ShouldBeNull();
        }

        [Fact]
        public void Sessions_AreIsolated()
        {
            var store = new SessionSelectionStore();
            store.Set("session-1", "inst-A");
            store.Set("session-2", "inst-B");
            store.Get("session-1").ShouldBe("inst-A");
            store.Get("session-2").ShouldBe("inst-B");
        }

        [Fact]
        public void Clear_Removes_Selection()
        {
            var store = new SessionSelectionStore();
            store.Set("session-1", "inst-A");
            store.Clear("session-1");
            store.Get("session-1").ShouldBeNull();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Get_NullOrEmpty_ReturnsNull(string? sessionId)
        {
            new SessionSelectionStore().Get(sessionId).ShouldBeNull();
        }

        [Theory]
        [InlineData(null, "inst-A")]
        [InlineData("", "inst-A")]
        [InlineData("session-1", null)]
        [InlineData("session-1", "")]
        public void Set_RejectsEmptyArguments(string? sessionId, string? instanceId)
        {
            Should.Throw<ArgumentException>(() => new SessionSelectionStore().Set(sessionId!, instanceId!));
        }
    }
}
