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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace com.IvanMurzak.McpPlugin.Common.Model
{
    public class ResponseListTool
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// Whether the tool is listed in MCP <c>tools/list</c> (custom property). A hidden tool still runs
        /// through <c>tools/call</c>. Not serialized itself: the wire carries it as <see cref="Enabled"/>.
        /// </summary>
        [JsonIgnore]
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Wire member of <see cref="Visible"/> (its old name). It stays the serialized member, under whatever
        /// naming policy the hub uses, so plugins and servers of older McpPlugin versions keep exchanging catalogues.
        /// </summary>
        [Obsolete("Renamed to Visible: the flag only controls whether the tool is listed in tools/list; tools/call still runs it. Kept as the wire member.")]
        public bool Enabled { get => Visible; set => Visible = value; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public JsonElement InputSchema { get; set; }
        public JsonElement? OutputSchema { get; set; }
        public bool? ReadOnlyHint { get; set; }
        public bool? DestructiveHint { get; set; }
        public bool? IdempotentHint { get; set; }
        public bool? OpenWorldHint { get; set; }

        /// <summary>
        /// Optional concise blurb sourced from the tool's <c>AiSkillDescription</c> attribute.
        /// Surfaced on the wire so the MCP server can emit it as a <c>tools/list</c>
        /// <c>_meta.skillDescription</c> key. <see langword="null"/> when the tool declares none.
        /// </summary>
        /// <remarks>
        /// MUST stay a property: the SignalR payload serializer is configured by
        /// <see cref="SignalR_JsonConfiguration"/>, which does NOT propagate
        /// <c>JsonSerializerOptions.IncludeFields</c> — a field would compile and pass
        /// in-process tests while silently vanishing on the wire.
        /// </remarks>
        public string? SkillDescription { get; set; }

        /// <summary>
        /// Optional long-form markdown sourced from the tool's <c>AiSkillBody</c> attribute.
        /// Surfaced on the wire so the MCP server can emit it as a <c>tools/list</c>
        /// <c>_meta.skillBody</c> key. <see langword="null"/> when the tool declares none.
        /// </summary>
        /// <remarks>
        /// MUST stay a property, for the same reason as <see cref="SkillDescription"/>.
        /// </remarks>
        public string? SkillBody { get; set; }

        /// <summary>
        /// The connections that register this tool (<see cref="ToolTarget"/>); null for
        /// <see cref="ToolTarget.Both"/> and for plugins that predate it. The server routes a call of a
        /// <see cref="ToolTarget.Runtime"/> tool to the runtime connection while an editor is connected too.
        /// </summary>
        public ToolTarget? Target { get; set; }

        public ResponseListTool() { }
    }
}
