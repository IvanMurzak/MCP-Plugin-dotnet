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
    /// MCP call still succeeds, and every one of them now returns an EMPTY catalog, mirroring
    /// <see cref="ExtensionsTool.SetError(ListToolsResult, string)"/>. Two earlier deviations are
    /// both closed: the resource pair used to <c>throw new Exception(message)</c>, surfacing to the
    /// client as a hard JSON-RPC <c>-32603</c> whenever the engine plugin had not attached yet
    /// (issue #193, defect 2); and the prompt overload
    /// (<see cref="ExtensionsPrompt.SetError(ListPromptsResult, string)"/>) used to fabricate a
    /// single synthetic prompt named <c>"Error"</c> carrying the internal message, which MCP clients
    /// rendered as a real, selectable prompt. In every case the message is not lost — the router
    /// logs it at Warn before degrading.
    /// </summary>
    [Collection("McpPlugin.Server")]
    public class ListSetErrorDegradationTests
    {
        // What the *_ReturnsEmptyCatalog_DoesNotThrow cases establish, precisely: SetError does NOT
        // throw (the defect-2 regression guard) and returns the SAME instance with a non-null catalog.
        // They do NOT establish that the catalog was emptied — a freshly-constructed List*Result
        // already exposes a non-null EMPTY list, so the count assertion below holds even for a SetError
        // that clears nothing. The emptying is pinned by the *_ClearsAnyAlreadyCollectedEntries cases,
        // which start from a POPULATED catalog and are the only ones that can observe it.

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

        /// <summary>The prompt overload no longer fabricates a client-visible entry.</summary>
        [Fact]
        public void ListPrompts_SetError_ReturnsEmptyCatalog_DoesNotThrow()
        {
            var result = new ListPromptsResult();

            var returned = Should.NotThrow(() => result.SetError("[Error] plugin not connected"));

            returned.ShouldBeSameAs(result);
            returned.Prompts.ShouldNotBeNull();
            returned.Prompts.Count.ShouldBe(0);
        }

        /// <summary>
        /// The defect this pins: <c>SetError</c> used to REPLACE the catalog with a single synthetic
        /// <c>Prompt { Name = "Error", Description = message }</c>. Starting from a populated catalog
        /// makes both halves observable at once — the stale entry must go, and no fabricated entry
        /// may take its place. Emptiness is asserted rather than "contains nothing named Error", so a
        /// fabrication under ANY name fails this case.
        /// </summary>
        [Fact]
        public void ListPrompts_SetError_ClearsAnyAlreadyCollectedEntries()
        {
            var result = new ListPromptsResult
            {
                Prompts = new List<Prompt>
                {
                    new Prompt { Name = "stale", Description = "collected before the failure" }
                }
            };

            result.SetError("Invoke 'RunListPrompts': Failed to invoke 'RequestListPrompts' after 10 retries.");

            result.Prompts.Count.ShouldBe(0);
        }

        [Fact]
        public void ListPrompts_SetError_OnNullTarget_ThrowsArgumentNullException()
        {
            ListPromptsResult? target = null;

            Should.Throw<ArgumentNullException>(() => target!.SetError("boom"));
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
