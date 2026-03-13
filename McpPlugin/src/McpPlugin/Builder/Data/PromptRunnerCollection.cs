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
    public class PromptRunnerCollection : Dictionary<string, IRunPrompt>
    {
        readonly Reflector reflector;
        readonly ILogger? _logger;

        public PromptRunnerCollection(Reflector reflector, ILogger? logger)
        {
            this.reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _logger = logger;
            _logger?.LogTrace("Ctor.");
        }
        public PromptRunnerCollection Add(IEnumerable<PromptMethodData> methods)
        {
            foreach (var method in methods.Where(resource => !string.IsNullOrEmpty(resource.Attribute?.Name)))
            {
                var attr = method.Attribute;
                this[attr.Name!] = method.MethodInfo.IsStatic
                    ? RunPrompt.CreateFromStaticMethod(reflector, attr.Name, _logger, method.MethodInfo, enabled: attr.EnabledValue)
                    : RunPrompt.CreateFromClassMethod(reflector, attr.Name, _logger, method.ClassType, method.MethodInfo, enabled: attr.EnabledValue);
            }
            return this;
        }
        public PromptRunnerCollection Add(IDictionary<string, IRunPrompt> runners)
        {
            if (runners == null)
                throw new ArgumentNullException(nameof(runners));

            foreach (var runner in runners)
                Add(runner.Key, runner.Value);

            return this;
        }
    }
}
