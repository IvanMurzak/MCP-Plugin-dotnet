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
    /// This text is <b>not</b> written into the YAML front-matter, but it CAN reach the MCP
    /// <c>tools/list</c> payload: the server emits it as the <c>_meta.skillBody</c> key on the
    /// tool's list entry for callers that opted in with the <c>X-McpPlugin-Skill-Meta: 1</c>
    /// request header — see <c>ExtensionsListMeta.BuildToolMeta</c>. That header is a payload gate,
    /// not a security boundary (any client may send it), so keep this text to content that is safe
    /// to publish to any connected MCP client.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AiSkillBodyAttribute : Attribute
    {
        public string Body { get; }

        public AiSkillBodyAttribute(string body)
        {
            Body = body ?? string.Empty;
        }
    }
}
