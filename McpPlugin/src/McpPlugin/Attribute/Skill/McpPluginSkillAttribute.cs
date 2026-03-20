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

namespace com.IvanMurzak.McpPlugin
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class McpPluginSkillAttribute : Attribute
    {
        public string Name { get; set; }
        public string? Description { get; set; }

        private bool _enabled = true;
        private bool _enabledSet;

        /// <summary>
        /// If set to false, the skill will be disabled by default when first discovered.
        /// When not set, the skill defaults to enabled.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; _enabledSet = true; }
        }

        /// <summary>Gets the Enabled value, or null if it was not explicitly set.</summary>
        public bool? EnabledValue => _enabledSet ? _enabled : null;

        public McpPluginSkillAttribute(string name, string? description = null)
        {
            Name = name;
            Description = description;
        }
    }
}
