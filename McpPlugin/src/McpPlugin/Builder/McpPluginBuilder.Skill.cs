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
        public virtual IMcpPluginBuilder WithSkills(params Type[] targetTypes)
            => WithSkills(targetTypes.AsEnumerable());

        public virtual IMcpPluginBuilder WithSkills(IEnumerable<Type> targetTypes)
        {
            if (targetTypes == null)
                throw new ArgumentNullException(nameof(targetTypes));

            foreach (var targetType in targetTypes)
                WithSkills(targetType);

            return this;
        }

        public virtual IMcpPluginBuilder WithSkills<T>()
            => WithSkills(typeof(T));

        public virtual IMcpPluginBuilder WithSkills(Type classType)
        {
            if (classType == null)
                throw new ArgumentNullException(nameof(classType));

            ThrowIfBuilt();

            _skillTypes.Add(classType);
            return this;
        }

        public virtual IMcpPluginBuilder WithSkillsFromAssembly(IEnumerable<Assembly> assemblies)
        {
            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
                WithSkillsFromAssembly(assembly);

            return this;
        }

        public virtual IMcpPluginBuilder WithSkillsFromAssembly(Assembly? assembly = null)
        {
            ThrowIfBuilt();
            assembly ??= Assembly.GetCallingAssembly();
            _skillAssemblies.Add(assembly);
            return this;
        }
    }
}
