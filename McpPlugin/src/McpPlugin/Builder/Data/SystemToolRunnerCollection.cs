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
using System.Collections.Generic;
using System.Linq;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// Collection of system tool runners — built from methods with
    /// <see cref="McpPluginToolAttribute.ToolType"/> set to <see cref="McpToolType.System"/>.
    /// </summary>
    public class SystemToolRunnerCollection : Dictionary<string, IRunTool>
    {
        readonly Reflector reflector;
        readonly ILogger? _logger;

        public SystemToolRunnerCollection(Reflector reflector, ILogger? logger)
        {
            this.reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _logger = logger;
            _logger?.LogTrace("Ctor.");
        }

        public SystemToolRunnerCollection Add(IEnumerable<ToolMethodData> methods)
        {
            foreach (var method in methods.Where(m => !string.IsNullOrEmpty(m.Attribute?.Name)))
            {
                var attr = method.Attribute;
                this[attr.Name] = method.MethodInfo.IsStatic
                    ? (IRunTool)RunTool.CreateFromStaticMethod(
                        reflector: reflector,
                        logger: _logger,
                        name: attr.Name,
                        methodInfo: method.MethodInfo,
                        title: attr.Title,
                        readOnlyHint: attr.ReadOnlyHintValue,
                        destructiveHint: attr.DestructiveHintValue,
                        idempotentHint: attr.IdempotentHintValue,
                        openWorldHint: attr.OpenWorldHintValue,
                        enabled: attr.EnabledValue)
                    : RunTool.CreateFromClassMethod(
                        reflector: reflector,
                        logger: _logger,
                        name: attr.Name,
                        classType: method.ClassType,
                        methodInfo: method.MethodInfo,
                        title: attr.Title,
                        readOnlyHint: attr.ReadOnlyHintValue,
                        destructiveHint: attr.DestructiveHintValue,
                        idempotentHint: attr.IdempotentHintValue,
                        openWorldHint: attr.OpenWorldHintValue,
                        enabled: attr.EnabledValue);
            }
            return this;
        }
    }
}
