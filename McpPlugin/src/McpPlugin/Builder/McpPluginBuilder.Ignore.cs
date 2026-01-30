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
using System.Reflection;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder
    {
        #region Ignore Assembly

        public McpPluginBuilder IgnoreAssembly(Assembly assembly)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Add(assembly);
            return this;
        }

        public McpPluginBuilder IgnoreAssembly(string assemblyName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblyNames.Add(assemblyName);
            return this;
        }

        public McpPluginBuilder IgnoreAssemblies(IEnumerable<Assembly> assemblies)
        {
            ThrowIfBuilt();
            foreach (var assembly in assemblies)
                _ignoreConfig.IgnoredAssemblies.Add(assembly);
            return this;
        }

        public McpPluginBuilder IgnoreAssemblies(params string[] assemblyNames)
        {
            ThrowIfBuilt();
            foreach (var name in assemblyNames)
                _ignoreConfig.IgnoredAssemblyNames.Add(name);
            return this;
        }

        #endregion

        #region Ignore Namespace

        public McpPluginBuilder IgnoreNamespace(string namespaceName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Add(namespaceName);
            return this;
        }

        public McpPluginBuilder IgnoreNamespaces(params string[] namespaceNames)
        {
            ThrowIfBuilt();
            foreach (var ns in namespaceNames)
                _ignoreConfig.IgnoredNamespaces.Add(ns);
            return this;
        }

        #endregion

        #region Remove Ignored Assembly

        public McpPluginBuilder RemoveIgnoredAssembly(Assembly assembly)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Remove(assembly);
            return this;
        }

        public McpPluginBuilder RemoveIgnoredAssembly(string assemblyName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblyNames.Remove(assemblyName);
            return this;
        }

        public McpPluginBuilder RemoveIgnoredAssemblies(IEnumerable<Assembly> assemblies)
        {
            ThrowIfBuilt();
            foreach (var assembly in assemblies)
                _ignoreConfig.IgnoredAssemblies.Remove(assembly);
            return this;
        }

        public McpPluginBuilder RemoveIgnoredAssemblies(params string[] assemblyNames)
        {
            ThrowIfBuilt();
            foreach (var name in assemblyNames)
                _ignoreConfig.IgnoredAssemblyNames.Remove(name);
            return this;
        }

        #endregion

        #region Remove Ignored Namespace

        public McpPluginBuilder RemoveIgnoredNamespace(string namespaceName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Remove(namespaceName);
            return this;
        }

        public McpPluginBuilder RemoveIgnoredNamespaces(params string[] namespaceNames)
        {
            ThrowIfBuilt();
            foreach (var ns in namespaceNames)
                _ignoreConfig.IgnoredNamespaces.Remove(ns);
            return this;
        }

        #endregion

        #region Clear Ignored

        public McpPluginBuilder ClearIgnoredAssemblies()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Clear();
            _ignoreConfig.IgnoredAssemblyNames.Clear();
            return this;
        }

        public McpPluginBuilder ClearIgnoredNamespaces()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Clear();
            return this;
        }

        public McpPluginBuilder ClearAllIgnored()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Clear();
            _ignoreConfig.IgnoredAssemblyNames.Clear();
            _ignoreConfig.IgnoredNamespaces.Clear();
            return this;
        }

        #endregion

        #region Statistics

        public int GetIgnoredAssembliesCount()
        {
            var uniqueAssemblies = new HashSet<Assembly>(_toolAssemblies);
            uniqueAssemblies.UnionWith(_promptAssemblies);
            uniqueAssemblies.UnionWith(_resourceAssemblies);

            int count = 0;
            foreach (var assembly in uniqueAssemblies)
            {
                if (_ignoreConfig.IsIgnored(assembly))
                    count++;
            }
            return count;
        }

        public int GetIgnoredTypesCount()
        {
            int count = 0;

            // 1. Explicitly registered types
            var uniqueTypes = new HashSet<Type>(_toolTypes);
            uniqueTypes.UnionWith(_promptTypes);
            uniqueTypes.UnionWith(_resourceTypes);

            foreach (var type in uniqueTypes)
            {
                if (_ignoreConfig.IsIgnored(type))
                    count++;
            }

            // 2. Types in registered assemblies
            var uniqueAssemblies = new HashSet<Assembly>(_toolAssemblies);
            uniqueAssemblies.UnionWith(_promptAssemblies);
            uniqueAssemblies.UnionWith(_resourceAssemblies);

            foreach (var assembly in uniqueAssemblies)
            {
                // If the assembly itself is ignored, we skip it (scanning logic skips it)
                if (_ignoreConfig.IsIgnored(assembly))
                    continue;

                try
                {
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (_ignoreConfig.IsIgnored(type))
                            count++;
                    }
                }
                catch
                {
                    // Ignore load errors, consistent with scanner
                }
            }

            return count;
        }

        #endregion
    }
}
