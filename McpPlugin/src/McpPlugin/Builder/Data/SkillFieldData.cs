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
using System.Reflection;

namespace com.IvanMurzak.McpPlugin
{
    public class SkillFieldData
    {
        public string Name => Attribute.Name;
        public Type ClassType { get; set; }
        public FieldInfo FieldInfo { get; set; }
        public McpPluginSkillAttribute Attribute { get; set; }
        public string Content { get; set; }

        public SkillFieldData(Type classType, FieldInfo fieldInfo, McpPluginSkillAttribute attribute, string content)
        {
            ClassType = classType;
            FieldInfo = fieldInfo;
            Attribute = attribute;
            Content = content;
        }
    }
}
