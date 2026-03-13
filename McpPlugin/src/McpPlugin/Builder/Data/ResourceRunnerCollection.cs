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
    public class ResourceRunnerCollection : Dictionary<string, IRunResource>
    {
        readonly Reflector reflector;
        readonly ILogger? _logger;

        public ResourceRunnerCollection(Reflector reflector, ILogger? logger)
        {
            this.reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
            _logger = logger;
            _logger?.LogTrace("Ctor.");
        }
        public ResourceRunnerCollection Add(IEnumerable<ResourceMethodData> methods)
        {
            foreach (var method in methods.Where(resource => !string.IsNullOrEmpty(resource.Attribute?.Name)))
            {
                var attr = method.Attribute;
                this[attr.Name!] = new RunResource
                (
                    route: string.IsNullOrWhiteSpace(attr!.Route) ? throw new InvalidOperationException($"Method {method.ClassType.FullName}{method.GetContentMethod.Name} does not have a 'route'.") : attr.Route,
                    name: attr.Name ?? throw new InvalidOperationException($"Method {method.ClassType.FullName}{method.GetContentMethod.Name} does not have a 'name'."),
                    description: attr.Description,
                    mimeType: attr.MimeType,
                    runnerGetContent: method.GetContentMethod.IsStatic
                        ? RunResourceContent.CreateFromStaticMethod(reflector, _logger, method.GetContentMethod)
                        : RunResourceContent.CreateFromClassMethod(reflector, _logger, method.ClassType, method.GetContentMethod),
                    runnerListContext: method.ListResourcesMethod.IsStatic
                        ? RunResourceList.CreateFromStaticMethod(reflector, _logger, method.ListResourcesMethod)
                        : RunResourceList.CreateFromClassMethod(reflector, _logger, method.ClassType, method.ListResourcesMethod),
                    enabled: attr.EnabledValue
                );
            }
            return this;
        }
        public ResourceRunnerCollection Add(IDictionary<string, IRunResource> runners)
        {
            if (runners == null)
                throw new ArgumentNullException(nameof(runners));

            foreach (var runner in runners)
                Add(runner.Key, runner.Value);

            return this;
        }
    }
}
