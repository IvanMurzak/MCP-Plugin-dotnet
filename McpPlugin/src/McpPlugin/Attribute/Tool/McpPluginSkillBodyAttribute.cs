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
    /// Provides long-form markdown that is injected into the SKILL.md body, between the description
    /// paragraph and the <c>## How to Call</c> section. Use this to carry rich content (code samples,
    /// usage notes, suggestions) that would otherwise blow past the 1024-character cap on the YAML
    /// <c>description:</c> field.
    /// <para>
    /// This text is <b>not</b> written into the YAML front-matter and does <b>not</b> affect the MCP
    /// <c>tools/list</c> payload — it only enriches the generated SKILL.md.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class McpPluginSkillBodyAttribute : Attribute
    {
        public string Body { get; }

        public McpPluginSkillBodyAttribute(string body)
        {
            Body = body ?? string.Empty;
        }
    }
}
