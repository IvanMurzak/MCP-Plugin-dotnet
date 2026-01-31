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
    public class McpPluginBuilderIgnoreConfig
    {
        internal HashSet<string> IgnoredAssemblyNames { get; } = new(StringComparer.Ordinal);
        internal HashSet<Assembly> IgnoredAssemblies { get; } = new();
        internal HashSet<string> IgnoredNamespaces { get; } = new();

        // Caches for O(1) lookup after first check
        private readonly Dictionary<Assembly, bool> _assemblyIgnoreCache = new();
        private readonly Dictionary<string, bool> _namespaceIgnoreCache = new(StringComparer.Ordinal);

        internal bool IsIgnored(Assembly assembly)
        {
            if (_assemblyIgnoreCache.TryGetValue(assembly, out var cached))
                return cached;

            var result = CheckAssemblyIgnored(assembly);
            _assemblyIgnoreCache[assembly] = result;
            return result;
        }

        private bool CheckAssemblyIgnored(Assembly assembly)
        {
            if (IgnoredAssemblies.Contains(assembly))
                return true;

            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            foreach (var ignoreName in IgnoredAssemblyNames)
            {
                if (assemblyName.StartsWith(ignoreName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal bool IsIgnored(Type type)
            => IsNamespaceIgnored(type.Namespace);

        private bool IsNamespaceIgnored(string? typeNamespace)
        {
            if (string.IsNullOrEmpty(typeNamespace))
                return false;

            if (_namespaceIgnoreCache.TryGetValue(typeNamespace, out var cached))
                return cached;

            var result = CheckNamespaceIgnored(typeNamespace);
            _namespaceIgnoreCache[typeNamespace] = result;
            return result;
        }

        private bool CheckNamespaceIgnored(string typeNamespace)
        {
            foreach (var ignoreName in IgnoredNamespaces)
            {
                if (typeNamespace.StartsWith(ignoreName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Invalidates all cached ignore lookups. Must be called whenever
        /// the ignore lists (assemblies, assembly names, or namespaces) are mutated.
        /// </summary>
        internal void InvalidateCaches()
        {
            _assemblyIgnoreCache.Clear();
            _namespaceIgnoreCache.Clear();
        }
    }
}
