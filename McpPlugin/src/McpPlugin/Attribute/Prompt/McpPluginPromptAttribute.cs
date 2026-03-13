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
using com.IvanMurzak.McpPlugin.Common.Model;

namespace com.IvanMurzak.McpPlugin
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class McpPluginPromptAttribute : Attribute
    {
        public string Name { get; set; } = string.Empty;
        public Role Role { get; set; } = Role.User;

        // Not used for now
        // public string? Title { get; set; }

        private bool _enabled;
        private bool _enabledSet;

        /// <summary>
        /// If set to false, the prompt will be disabled by default when first discovered.
        /// When not set, the prompt defaults to enabled.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; _enabledSet = true; }
        }

        /// <summary>Gets the Enabled value, or null if it was not explicitly set.</summary>
        public bool? EnabledValue => _enabledSet ? _enabled : null;

        public McpPluginPromptAttribute() { }
    }
}
