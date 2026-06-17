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
using System.Collections.Generic;

namespace com.IvanMurzak.McpPlugin.AgentConfig
{
    /// <summary>
    /// The closed element vocabulary every engine's configurator UI already speaks. A
    /// consuming engine renders each item with its own widget toolkit (UIToolkit in Unity,
    /// Slate in Unreal, Control nodes in Godot) — this DTO carries only the engine-agnostic
    /// content, never a UI handle.
    /// </summary>
    public enum ConfigurationItemKind
    {
        /// <summary>Plain descriptive text.</summary>
        Description,
        /// <summary>A warning line (e.g. "Authorization is enabled. Set the env var first").</summary>
        Warning,
        /// <summary>An alert / call-to-action line.</summary>
        Alert,
        /// <summary>A non-editable, copy-pasteable value (command line, JSON/TOML snippet, path).</summary>
        ReadOnlyField,
        /// <summary>An editable value the user can change (the Custom agent's editable config-path).</summary>
        EditableField,
        /// <summary>
        /// A clickable open-URL link (download / tutorial). The link target lives in
        /// <see cref="ConfigurationItem.Url"/>; <see cref="ConfigurationItem.Text"/> is the
        /// display label. Mirrors Unity's download/tutorial links.
        /// </summary>
        Link
    }

    /// <summary>
    /// A single rendered element inside a <see cref="ConfigurationSection"/>.
    /// </summary>
    public sealed class ConfigurationItem
    {
        /// <summary>How this item should be rendered.</summary>
        public ConfigurationItemKind Kind { get; }

        /// <summary>
        /// The item's text. For <see cref="ConfigurationItemKind.Description"/> /
        /// <see cref="ConfigurationItemKind.Warning"/> / <see cref="ConfigurationItemKind.Alert"/>
        /// it is the message; for <see cref="ConfigurationItemKind.ReadOnlyField"/> /
        /// <see cref="ConfigurationItemKind.EditableField"/> it is the field value.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// The open-URL target for a <see cref="ConfigurationItemKind.Link"/> item; null for
        /// every other kind. <see cref="Text"/> carries the link's display label.
        /// </summary>
        public string? Url { get; }

        public ConfigurationItem(ConfigurationItemKind kind, string text, string? url = null)
        {
            Kind = kind;
            Text = text;
            Url = url;
        }

        public static ConfigurationItem Description(string text) => new(ConfigurationItemKind.Description, text);
        public static ConfigurationItem Warning(string text) => new(ConfigurationItemKind.Warning, text);
        public static ConfigurationItem Alert(string text) => new(ConfigurationItemKind.Alert, text);
        public static ConfigurationItem ReadOnlyField(string value) => new(ConfigurationItemKind.ReadOnlyField, value);
        public static ConfigurationItem EditableField(string value) => new(ConfigurationItemKind.EditableField, value);
        public static ConfigurationItem Link(string label, string url) => new(ConfigurationItemKind.Link, label, url);
    }

    /// <summary>
    /// A foldout / collapsible group of <see cref="ConfigurationItem"/>s (e.g. "Start",
    /// "Manual Configuration Steps", "Troubleshooting"). Mirrors the engines' existing
    /// foldout sections.
    /// </summary>
    public sealed class ConfigurationSection
    {
        /// <summary>The section heading shown on the foldout.</summary>
        public string Heading { get; }

        /// <summary>Whether the section is expanded by default (the first/"Start" section usually is).</summary>
        public bool ExpandedFirst { get; }

        /// <summary>The ordered items inside the section.</summary>
        public IReadOnlyList<ConfigurationItem> Items { get; }

        public ConfigurationSection(string heading, bool expandedFirst, IReadOnlyList<ConfigurationItem> items)
        {
            Heading = heading;
            ExpandedFirst = expandedFirst;
            Items = items;
        }
    }

    /// <summary>
    /// The three configuration states a configurator distinguishes for the active transport,
    /// mirroring Unity's absent / configured-current / configured-but-stale distinction
    /// (<c>AiAgentConfigurator.IsReconfigureNeeded</c>). Consumers render a "Reconfigure
    /// needed" alert for <see cref="ReconfigureNeeded"/>.
    /// </summary>
    public enum ConfiguratorStatus
    {
        /// <summary>No MCP config entry is present on disk for this transport.</summary>
        NotConfigured,
        /// <summary>A config entry is present and matches the current settings.</summary>
        Configured,
        /// <summary>A config entry is present but is outdated — it no longer matches the current settings.</summary>
        ReconfigureNeeded
    }

    /// <summary>
    /// Engine-agnostic description of a configurator's UI for one transport, plus the
    /// agent identity and current status. A consuming engine reads this to render the
    /// agent panel without the shared library taking any UI dependency.
    /// </summary>
    public sealed class AgentConfiguratorDescription
    {
        /// <summary>Display name of the AI agent (e.g. "Claude Code").</summary>
        public string AgentName { get; }

        /// <summary>Stable identifier of the AI agent (e.g. "claude-code").</summary>
        public string AgentId { get; }

        /// <summary>Icon file NAME only (e.g. "claude-64.png"), never the bytes. Null when no icon.</summary>
        public string? IconName { get; }

        /// <summary>Whether the MCP config file currently has this server entry (detected on disk).</summary>
        public bool IsConfigured { get; }

        /// <summary>
        /// The three-state configuration status for the described transport (absent /
        /// configured-current / configured-but-stale). <see cref="ConfiguratorStatus.ReconfigureNeeded"/>
        /// means a config entry exists on disk but no longer matches the current settings.
        /// </summary>
        public ConfiguratorStatus Status { get; }

        /// <summary>
        /// Open-URL links the consuming engine should render (download / tutorial). Each item is
        /// a <see cref="ConfigurationItemKind.Link"/> carrying a label (<see cref="ConfigurationItem.Text"/>)
        /// and a target (<see cref="ConfigurationItem.Url"/>). Empty when the agent declares none.
        /// </summary>
        public IReadOnlyList<ConfigurationItem> Links { get; }

        /// <summary>
        /// Whether the agent's tooling is considered installed/available. Engines that cannot
        /// detect installation leave this false; it is part of the closed status vocabulary so
        /// every engine reports the same shape.
        /// </summary>
        public bool IsInstalled { get; }

        /// <summary>The ordered UI sections to render.</summary>
        public IReadOnlyList<ConfigurationSection> Sections { get; }

        public AgentConfiguratorDescription(
            string agentName,
            string agentId,
            string? iconName,
            bool isConfigured,
            bool isInstalled,
            IReadOnlyList<ConfigurationSection> sections,
            ConfiguratorStatus status = ConfiguratorStatus.NotConfigured,
            IReadOnlyList<ConfigurationItem>? links = null)
        {
            AgentName = agentName;
            AgentId = agentId;
            IconName = iconName;
            IsConfigured = isConfigured;
            IsInstalled = isInstalled;
            Sections = sections;
            Status = status;
            Links = links ?? System.Array.Empty<ConfigurationItem>();
        }
    }
}
