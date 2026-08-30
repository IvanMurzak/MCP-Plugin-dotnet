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
using System.Collections.Generic;
using com.IvanMurzak.McpPlugin.Common.Hub.Client;
using R3;

namespace com.IvanMurzak.McpPlugin
{
    public interface IToolManager : IClientToolHub, IDisposable
    {
        Observable<Unit> OnToolsUpdated { get; }
        int EnabledToolsCount { get; }
        int TotalToolsCount { get; }
        ulong ToolCallsCount => 0;
        
        /// <summary>
        /// Gets the total token count for all enabled tools.
        /// This is calculated as the sum of TokenCount for each enabled tool.
        /// 
        /// <para>
        /// <b>Note:</b> This property recalculates the sum on each access by iterating through all enabled tools.
        /// For typical use cases with a reasonable number of tools, this should be performant. If called very
        /// frequently in performance-critical paths, consider caching the result.
        /// </para>
        /// </summary>
        int EnabledToolsTokenCount { get; }

        /// <summary>
        /// The portion of <see cref="EnabledToolsTokenCount"/> contributed by the enabled tools' published
        /// skill metadata (<c>_meta.skillDescription</c> / <c>_meta.skillBody</c> on every
        /// <c>tools/list</c> entry), including its JSON key overhead.
        /// <c>EnabledToolsTokenCount - EnabledToolsSkillMetadataTokenCount</c> is what the catalogue
        /// weighed before that metadata reached the wire, so the two together answer "how much of the
        /// catalogue is skill metadata?" without changing the meaning of the total.
        /// <para>
        /// <b>Note:</b> that subtraction reads the two properties independently, and each re-walks the tool
        /// set on access (see <see cref="EnabledToolsTokenCount"/>). Registering or enabling a tool between
        /// the two reads therefore yields a difference computed from two different catalogues. Take both
        /// figures at a point where the catalogue is not being mutated.
        /// </para>
        /// <para>
        /// Defaulted to <c>0</c> so an existing external <see cref="IToolManager"/> implementation keeps
        /// compiling; <see cref="McpToolManager"/> implements it.
        /// </para>
        /// </summary>
        int EnabledToolsSkillMetadataTokenCount => 0;

        IEnumerable<IRunTool> GetAllTools();
        bool HasTool(string name);
        bool AddTool(string name, IRunTool runner);
        bool RemoveTool(string name);
        bool IsToolEnabled(string name);
        bool SetToolEnabled(string name, bool enabled);
    }
}
