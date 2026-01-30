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
using System.Reflection;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder
    {
        public McpPluginBuilder WithResources(params Type[] targetTypes)
            => WithResources(targetTypes.AsEnumerable());

        public McpPluginBuilder WithResources(IEnumerable<Type> targetTypes)
        {
            if (targetTypes == null)
                throw new ArgumentNullException(nameof(targetTypes));

            foreach (var targetType in targetTypes)
                WithResources(targetType);

            return this;
        }

        public McpPluginBuilder WithResources<T>()
            => WithResources(typeof(T));

        public McpPluginBuilder WithResources(Type classType)
        {
            if (classType == null)
                throw new ArgumentNullException(nameof(classType));

            ThrowIfBuilt();

            // Store type for lazy processing - ignore filtering happens at Build() time
            _resourceTypes.Add(classType);
            return this;
        }

        public McpPluginBuilder WithResourcesFromAssembly(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
                WithResourcesFromAssembly(assembly);

            return this;
        }

        public McpPluginBuilder WithResourcesFromAssembly(Assembly? assembly = null)
        {
            ThrowIfBuilt();
            assembly ??= Assembly.GetCallingAssembly();
            _resourceAssemblies.Add(assembly);
            return this;
        }
    }
}
