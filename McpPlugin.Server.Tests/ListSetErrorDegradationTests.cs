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
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Every list-shaped <c>SetError</c> overload in the server DEGRADES — it returns an empty
    /// catalog so the MCP call still succeeds. The two resource overloads used to be the odd ones
    /// out: they were implemented as <c>throw new Exception(message)</c>, which surfaced to the
    /// client as a hard JSON-RPC <c>-32603</c> whenever the engine plugin had not attached yet
    /// (issue #193, defect 2).
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ListSetErrorDegradationTests
    {
        [Fact]
        public void ListResources_SetError_ReturnsEmptyCatalog_DoesNotThrow()
        {
            var result = new ListResourcesResult();

            var returned = Should.NotThrow(() => result.SetError("[Error] plugin not connected"));

            returned.ShouldBeSameAs(result);
            returned.Resources.ShouldNotBeNull();
            returned.Resources.Count.ShouldBe(0);
        }

        [Fact]
        public void ListResourceTemplates_SetError_ReturnsEmptyCatalog_DoesNotThrow()
        {
            var result = new ListResourceTemplatesResult();

            var returned = Should.NotThrow(() => result.SetError("[Error] plugin not connected"));

            returned.ShouldBeSameAs(result);
            returned.ResourceTemplates.ShouldNotBeNull();
            returned.ResourceTemplates.Count.ShouldBe(0);
        }

        /// <summary>
        /// A previously-populated catalog is CLEARED, not left partially filled — a half-populated
        /// error result would present stale entries as if they were live. Mirrors what the tool path
        /// does to <c>ListToolsResult.Tools</c>.
        /// </summary>
        [Fact]
        public void ListResources_SetError_ClearsAnyAlreadyCollectedEntries()
        {
            var result = new ListResourcesResult
            {
                Resources = new System.Collections.Generic.List<Resource>
                {
                    new Resource { Uri = "stale://one", Name = "stale" }
                }
            };

            result.SetError("[Error] plugin not connected");

            result.Resources.Count.ShouldBe(0);
        }

        [Fact]
        public void ListResourceTemplates_SetError_ClearsAnyAlreadyCollectedEntries()
        {
            var result = new ListResourceTemplatesResult
            {
                ResourceTemplates = new System.Collections.Generic.List<ResourceTemplate>
                {
                    new ResourceTemplate { UriTemplate = "stale://{id}", Name = "stale" }
                }
            };

            result.SetError("[Error] plugin not connected");

            result.ResourceTemplates.Count.ShouldBe(0);
        }

        [Fact]
        public void ListResources_SetError_OnNullTarget_ThrowsArgumentNullException()
        {
            ListResourcesResult? target = null;

            Should.Throw<ArgumentNullException>(() => target!.SetError("boom"));
        }

        [Fact]
        public void ListResourceTemplates_SetError_OnNullTarget_ThrowsArgumentNullException()
        {
            ListResourceTemplatesResult? target = null;

            Should.Throw<ArgumentNullException>(() => target!.SetError("boom"));
        }
    }
}
