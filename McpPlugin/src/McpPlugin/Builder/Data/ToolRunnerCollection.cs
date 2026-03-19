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
    public class ToolRunnerCollection : Dictionary<string, IRunTool>
    {
        readonly Reflector reflector;
        readonly ILogger? _logger;

        public ToolRunnerCollection(Reflector reflector, ILogger? logger)
        {
            this.reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _logger = logger;
            _logger?.LogTrace("Ctor.");
        }
        public ToolRunnerCollection Add(IEnumerable<ToolMethodData> methods)
        {
            foreach (var method in methods.Where(resource => !string.IsNullOrEmpty(resource.Attribute?.Name)))
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
                        enabled: attr.EnabledValue,
                        toolType: attr.ToolType)
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
                        enabled: attr.EnabledValue,
                        toolType: attr.ToolType);
            }
            return this;
        }
        public ToolRunnerCollection Add(IDictionary<string, IRunTool> runners)
        {
            if (runners == null)
                throw new ArgumentNullException(nameof(runners));

            foreach (var runner in runners)
                Add(runner.Key, runner.Value);

            return this;
        }
    }
}
