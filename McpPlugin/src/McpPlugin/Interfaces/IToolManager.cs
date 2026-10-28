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
        /// <summary>Number of tools listed in <c>tools/list</c>. Hidden tools still run through <c>tools/call</c>.</summary>
        int VisibleToolsCount { get; }
        int TotalToolsCount { get; }
        ulong ToolCallsCount => 0;

        /// <summary>
        /// EXPERIMENTAL. The data layer of the reflected tools (<see cref="IToolBinding"/>): agent-facing schemas, argument
        /// conversion and results; null = ReflectorNet. Managers that do not support bindings ignore it.
        /// </summary>
        IToolBinding? ToolBinding { get => null; set { } }
        
        /// <summary>
        /// Gets the total token count for all visible tools (the ones listed in <c>tools/list</c>).
        /// This is calculated as the sum of TokenCount for each visible tool.
        /// 
        /// <para>
        /// <b>Note:</b> This property recalculates the sum on each access by iterating through all visible tools.
        /// For typical use cases with a reasonable number of tools, this should be performant. If called very
        /// frequently in performance-critical paths, consider caching the result.
        /// </para>
        /// </summary>
        int VisibleToolsTokenCount { get; }

        /// <summary>
        /// The portion of <see cref="VisibleToolsTokenCount"/> contributed by the visible tools' published
        /// skill metadata (<c>_meta.skillDescription</c> / <c>_meta.skillBody</c> on every
        /// <c>tools/list</c> entry), including its JSON key overhead.
        /// <c>VisibleToolsTokenCount - VisibleToolsSkillMetadataTokenCount</c> is what the catalogue
        /// weighed before that metadata reached the wire, so the two together answer "how much of the
        /// catalogue is skill metadata?" without changing the meaning of the total.
        /// <para>
        /// <b>Note:</b> that subtraction reads the two properties independently, and each re-walks the tool
        /// set on access (see <see cref="VisibleToolsTokenCount"/>). Registering or showing a tool between
        /// the two reads therefore yields a difference computed from two different catalogues. Take both
        /// figures at a point where the catalogue is not being mutated.
        /// </para>
        /// <para>
        /// Defaulted to <c>0</c> so an existing external <see cref="IToolManager"/> implementation keeps
        /// compiling; <see cref="McpToolManager"/> implements it.
        /// </para>
        /// </summary>
        int VisibleToolsSkillMetadataTokenCount => 0;

        IEnumerable<IRunTool> GetAllTools();
        bool HasTool(string name);
        bool AddTool(string name, IRunTool runner);
        bool RemoveTool(string name);

        /// <summary>Whether the tool is listed in <c>tools/list</c>. False when the tool does not exist.</summary>
        bool IsToolVisible(string name);

        /// <summary>
        /// Shows or hides the tool in <c>tools/list</c> and fires the list-changed notification. Visibility is not
        /// an access control: a hidden tool still runs through <c>tools/call</c>. False when the tool does not exist.
        /// </summary>
        bool SetToolVisible(string name, bool visible);

        // Old names, kept as aliases until the next major release.
        [Obsolete("Renamed to VisibleToolsCount" + VisibilityRename.Note)]
        int EnabledToolsCount => VisibleToolsCount;
        [Obsolete("Renamed to VisibleToolsTokenCount" + VisibilityRename.Note)]
        int EnabledToolsTokenCount => VisibleToolsTokenCount;
        [Obsolete("Renamed to VisibleToolsSkillMetadataTokenCount" + VisibilityRename.Note)]
        int EnabledToolsSkillMetadataTokenCount => VisibleToolsSkillMetadataTokenCount;
        [Obsolete("Renamed to IsToolVisible" + VisibilityRename.Note)]
        bool IsToolEnabled(string name) => IsToolVisible(name);
        [Obsolete("Renamed to SetToolVisible" + VisibilityRename.Note)]
        bool SetToolEnabled(string name, bool enabled) => SetToolVisible(name, enabled);
    }
}
