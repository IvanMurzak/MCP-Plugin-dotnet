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
        public McpPluginBuilder WithTools(params Type[] targetTypes)
            => WithTools(targetTypes.AsEnumerable());

        public McpPluginBuilder WithTools(IEnumerable<Type> targetTypes)
        {
            if (targetTypes == null)
                throw new ArgumentNullException(nameof(targetTypes));

            foreach (var targetType in targetTypes)
                WithTools(targetType);

            return this;
        }

        public McpPluginBuilder WithTools<T>()
            => WithTools(typeof(T));

        public McpPluginBuilder WithTools(Type classType)
        {
            if (classType == null)
                throw new ArgumentNullException(nameof(classType));

            ThrowIfBuilt();

            // Store type for lazy processing - ignore filtering happens at Build() time
            _toolTypes.Add(classType);
            return this;
        }

        public McpPluginBuilder WithToolsFromAssembly(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
                WithToolsFromAssembly(assembly);

            return this;
        }

        public McpPluginBuilder WithToolsFromAssembly(Assembly? assembly = null)
        {
            ThrowIfBuilt();
            assembly ??= Assembly.GetCallingAssembly();
            _toolAssemblies.Add(assembly);
            return this;
        }
    }
}
