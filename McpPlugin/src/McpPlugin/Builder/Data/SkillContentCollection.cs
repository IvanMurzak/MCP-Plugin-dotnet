/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Linq;
using com.IvanMurzak.McpPlugin.Skills;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    public class SkillContentCollection : Dictionary<string, ISkillContent>
    {
        readonly ILogger? _logger;

        public SkillContentCollection(ILogger? logger = null)
        {
            _logger = logger;
            _logger?.LogTrace("Ctor.");
        }

        public SkillContentCollection Add(IEnumerable<SkillFieldData> fields)
        {
            foreach (var field in fields.Where(f => !string.IsNullOrEmpty(f.Attribute?.Name)))
            {
                var attr = field.Attribute;
                var enabled = attr.EnabledValue ?? true;

                if (!enabled)
                {
                    _logger?.LogDebug("Skill '{name}' is disabled, skipping.", attr.Name);
                    continue;
                }

                this[attr.Name] = new SkillContent(
                    name: attr.Name,
                    description: attr.Description,
                    content: field.Content,
                    enabled: enabled
                );
            }

            return this;
        }
    }
}
