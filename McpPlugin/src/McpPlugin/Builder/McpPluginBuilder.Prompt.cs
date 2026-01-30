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
        public McpPluginBuilder WithPrompts(params Type[] targetTypes)
            => WithPrompts(targetTypes.AsEnumerable());

        public McpPluginBuilder WithPrompts(IEnumerable<Type> targetTypes)
        {
            if (targetTypes == null)
                throw new ArgumentNullException(nameof(targetTypes));

            foreach (var targetType in targetTypes)
                WithPrompts(targetType);

            return this;
        }

        public McpPluginBuilder WithPrompts<T>()
            => WithPrompts(typeof(T));

        public McpPluginBuilder WithPrompts(Type classType)
        {
            if (classType == null)
                throw new ArgumentNullException(nameof(classType));

            ThrowIfBuilt();

            // Store type for lazy processing - ignore filtering happens at Build() time
            _promptTypes.Add(classType);
            return this;
        }

        public McpPluginBuilder WithPromptsFromAssembly(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
                WithPromptsFromAssembly(assembly);

            return this;
        }

        public McpPluginBuilder WithPromptsFromAssembly(Assembly? assembly = null)
        {
            ThrowIfBuilt();
            assembly ??= Assembly.GetCallingAssembly();
            _promptAssemblies.Add(assembly);
            return this;
        }
    }
}
