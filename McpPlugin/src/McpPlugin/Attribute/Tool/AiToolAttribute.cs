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
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AiToolAttribute : Attribute
    {
        public string Name { get; set; }
        public string? Title { get; set; }

        /// <summary>
        /// The type of tool. Standard tools are exposed to MCP clients;
        /// System tools are only available via the HTTP API.
        /// Defaults to <see cref="McpToolType.Standard"/>.
        /// </summary>
        public McpToolType ToolType { get; set; } = McpToolType.Standard;

        private bool _readOnlyHint;
        private bool _readOnlyHintSet;

        private bool _destructiveHint;
        private bool _destructiveHintSet;

        private bool _idempotentHint;
        private bool _idempotentHintSet;

        private bool _openWorldHint;
        private bool _openWorldHintSet;

        private bool _visible = true;
        private bool _visibleSet;

        /// <summary>
        /// If set to false, the tool is hidden from <c>tools/list</c> by default when first discovered.
        /// A hidden tool still runs through <c>tools/call</c>. When not set, the tool defaults to visible.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set { _visible = value; _visibleSet = true; }
        }

        /// <summary>Gets the Visible value, or null if it was not explicitly set.</summary>
        public bool? VisibleValue => _visibleSet ? _visible : null;

        private ToolTarget _target = ToolTarget.Both;
        private bool _targetSet;

        /// <summary>
        /// Which plugin connections register this tool (see <see cref="AiToolTypeAttribute.Target"/>). When not set,
        /// the class's <see cref="AiToolTypeAttribute.Target"/> applies (<see cref="ToolTarget.Both"/> by default).
        /// </summary>
        public ToolTarget Target
        {
            get => _target;
            set { _target = value; _targetSet = true; }
        }

        /// <summary>Gets the Target value, or null if it was not explicitly set.</summary>
        public ToolTarget? TargetValue => _targetSet ? _target : null;

        /// <summary>Old name of <see cref="Visible"/>.</summary>
        [Obsolete("Renamed to Visible" + VisibilityRename.Note)]
        public bool Enabled
        {
            get => Visible;
            set => Visible = value;
        }

        /// <summary>Old name of <see cref="VisibleValue"/>.</summary>
        [Obsolete("Renamed to VisibleValue" + VisibilityRename.Note)]
        public bool? EnabledValue => VisibleValue;

        /// <summary>
        /// If true, the tool only reads or queries data and does not modify system state.
        /// When not set, null is passed to the MCP SDK, which will apply its own default.
        /// </summary>
        public bool ReadOnlyHint
        {
            get => _readOnlyHint;
            set { _readOnlyHint = value; _readOnlyHintSet = true; }
        }

        /// <summary>
        /// If true, the tool may perform destructive updates (e.g., deleting data, overwriting files).
        /// When not set, null is passed to the MCP SDK, which will apply its own default.
        /// </summary>
        public bool DestructiveHint
        {
            get => _destructiveHint;
            set { _destructiveHint = value; _destructiveHintSet = true; }
        }

        /// <summary>
        /// If true, calling the tool multiple times with the same arguments will have no additional effect
        /// on its environment beyond the first call.
        /// When not set, null is passed to the MCP SDK, which will apply its own default.
        /// </summary>
        public bool IdempotentHint
        {
            get => _idempotentHint;
            set { _idempotentHint = value; _idempotentHintSet = true; }
        }

        /// <summary>
        /// If true, the tool may interact with an "open world" of external entities
        /// (e.g., the web, external APIs, or real-world systems).
        /// When not set, null is passed to the MCP SDK, which will apply its own default.
        /// </summary>
        public bool OpenWorldHint
        {
            get => _openWorldHint;
            set { _openWorldHint = value; _openWorldHintSet = true; }
        }

        /// <summary>Gets the ReadOnlyHint value, or null if it was not explicitly set.</summary>
        public bool? ReadOnlyHintValue => _readOnlyHintSet ? _readOnlyHint : null;

        /// <summary>Gets the DestructiveHint value, or null if it was not explicitly set.</summary>
        public bool? DestructiveHintValue => _destructiveHintSet ? _destructiveHint : null;

        /// <summary>Gets the IdempotentHint value, or null if it was not explicitly set.</summary>
        public bool? IdempotentHintValue => _idempotentHintSet ? _idempotentHint : null;

        /// <summary>Gets the OpenWorldHint value, or null if it was not explicitly set.</summary>
        public bool? OpenWorldHintValue => _openWorldHintSet ? _openWorldHint : null;

        public AiToolAttribute(string name, string? title = null)
        {
            Name = name;
            Title = title;
        }
    }
}
