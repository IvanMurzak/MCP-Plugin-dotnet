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
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;

namespace com.IvanMurzak.McpPlugin.Server.Tests
{
    /// <summary>
    /// Every list-shaped <c>SetError</c> overload in the server DEGRADES rather than throwing, so the
    /// MCP call still succeeds. The two resource overloads return an EMPTY catalog, mirroring
    /// <see cref="ExtensionsTool.SetError(ListToolsResult, string)"/>; the prompt overload
    /// (<see cref="ExtensionsPrompt.SetError(ListPromptsResult, string)"/>) is the deliberate odd one
    /// out and returns a single synthetic <c>Error</c> prompt carrying the message. The resource pair
    /// used to be the only list-shaped overloads that did NOT degrade: they were implemented as
    /// <c>throw new Exception(message)</c>, which surfaced to the client as a hard JSON-RPC
    /// <c>-32603</c> whenever the engine plugin had not attached yet (issue #193, defect 2).
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ListSetErrorDegradationTests
    {
        // What these two cases establish, precisely: SetError does NOT throw (the defect-2 regression
        // guard) and returns the SAME instance with a non-null catalog. They do NOT establish that the
        // catalog was emptied — a freshly-constructed List*Result already exposes a non-null EMPTY list,
        // so the count assertion below holds even for a SetError that clears nothing. The emptying is
        // pinned by the two *_ClearsAnyAlreadyCollectedEntries cases, which start from a POPULATED
        // catalog and are the only ones that can observe it.

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
                Resources = new List<Resource>
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
                ResourceTemplates = new List<ResourceTemplate>
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
