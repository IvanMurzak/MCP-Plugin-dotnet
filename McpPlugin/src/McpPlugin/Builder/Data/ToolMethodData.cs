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
using System.Linq;
using System.Reflection;
using com.IvanMurzak.McpPlugin.Common;

namespace com.IvanMurzak.McpPlugin
{
    public class ToolMethodData
    {
        public string Name => Attribute.Name;
        public Type ClassType { get; set; }
        public MethodInfo MethodInfo { get; set; }
        public AiToolAttribute Attribute { get; set; }

        /// <summary>
        /// Which plugin connections register the tool: the tool's own <see cref="AiToolAttribute.Target"/>, else its
        /// class's <see cref="AiToolTypeAttribute.Target"/>, else <see cref="ToolTarget.Both"/>.
        /// </summary>
        public ToolTarget Target => Attribute.TargetValue
            ?? ClassType?.GetCustomAttributes(typeof(AiToolTypeAttribute), inherit: false).Cast<AiToolTypeAttribute>().FirstOrDefault()?.Target
            ?? ToolTarget.Both;

        public ToolMethodData(Type classType, MethodInfo methodInfo, AiToolAttribute attribute)
        {
            ClassType = classType;
            MethodInfo = methodInfo;
            Attribute = attribute;
        }
    }
}
