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
    /// <summary>
    /// Provides a concise text written into the SKILL.md YAML <c>description:</c> field.
    /// The Skills file format used by Codex (and Anthropic Agent Skills) caps that field at 1024 characters,
    /// so when a tool's <see cref="System.ComponentModel.DescriptionAttribute"/> is too long for the YAML
    /// front-matter, decorate the same tool method with this attribute to provide a short summary.
    /// <para>
    /// The original <see cref="System.ComponentModel.DescriptionAttribute"/> remains the source of truth
    /// for the tool's <c>description</c> in the MCP <c>tools/list</c> payload. This attribute controls the
    /// YAML field AND is surfaced beside it as the <c>_meta.skillDescription</c> key on the tool's
    /// <c>tools/list</c> entry — but only for callers that opted in with the
    /// <c>X-McpPlugin-Skill-Meta: 1</c> request header, see <c>ExtensionsListMeta.BuildToolMeta</c>.
    /// That header is a payload gate, not a security boundary (any client may send it), so keep this
    /// text to content that is safe to publish to any connected MCP client.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AiSkillDescriptionAttribute : Attribute
    {
        public string Description { get; }

        public AiSkillDescriptionAttribute(string description)
        {
            Description = description ?? string.Empty;
        }
    }
}
