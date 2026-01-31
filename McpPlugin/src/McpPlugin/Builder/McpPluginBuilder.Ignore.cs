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

        /// <summary>
        /// Excludes the specified assembly from attribute scanning during build.
        /// Types from this assembly will not be registered as tools, prompts, or resources.
        /// </summary>
        /// <param name="assembly">The assembly to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreAssembly(Assembly assembly)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Add(assembly);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Excludes assemblies matching the specified name prefix from attribute scanning during build.
        /// Any assembly whose name starts with the provided string will be ignored.
        /// </summary>
        /// <param name="assemblyName">The assembly name or prefix to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreAssembly(string assemblyName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblyNames.Add(assemblyName);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Excludes multiple assemblies from attribute scanning during build.
        /// </summary>
        /// <param name="assemblies">The assemblies to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreAssemblies(IEnumerable<Assembly> assemblies)
        {
            ThrowIfBuilt();
            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                    continue;
                _ignoreConfig.IgnoredAssemblies.Add(assembly);
            }
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Excludes assemblies matching the specified name prefixes from attribute scanning during build.
        /// </summary>
        /// <param name="assemblyNames">The assembly names or prefixes to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreAssemblies(params string[] assemblyNames)
        {
            ThrowIfBuilt();
            foreach (var name in assemblyNames)
                _ignoreConfig.IgnoredAssemblyNames.Add(name);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        #endregion

        #region Ignore Namespace

        /// <summary>
        /// Excludes types in the specified namespace (and sub-namespaces) from attribute scanning during build.
        /// Types whose namespace starts with the provided string will be ignored.
        /// </summary>
        /// <param name="namespaceName">The namespace or namespace prefix to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreNamespace(string namespaceName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Add(namespaceName);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Excludes types in the specified namespaces (and their sub-namespaces) from attribute scanning during build.
        /// </summary>
        /// <param name="namespaceNames">The namespaces or namespace prefixes to ignore.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder IgnoreNamespaces(params string[] namespaceNames)
        {
            ThrowIfBuilt();
            foreach (var ns in namespaceNames)
                _ignoreConfig.IgnoredNamespaces.Add(ns);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        #endregion

        #region Remove Ignored Assembly

        /// <summary>
        /// Removes a previously ignored assembly, allowing it to be scanned again.
        /// </summary>
        /// <param name="assembly">The assembly to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredAssembly(Assembly assembly)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Remove(assembly);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Removes a previously ignored assembly name pattern, allowing matching assemblies to be scanned again.
        /// </summary>
        /// <param name="assemblyName">The assembly name or prefix to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredAssembly(string assemblyName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblyNames.Remove(assemblyName);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Removes multiple previously ignored assemblies, allowing them to be scanned again.
        /// </summary>
        /// <param name="assemblies">The assemblies to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredAssemblies(IEnumerable<Assembly> assemblies)
        {
            ThrowIfBuilt();
            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                    continue;
                _ignoreConfig.IgnoredAssemblies.Remove(assembly);
            }
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Removes multiple previously ignored assembly name patterns, allowing matching assemblies to be scanned again.
        /// </summary>
        /// <param name="assemblyNames">The assembly names or prefixes to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredAssemblies(params string[] assemblyNames)
        {
            ThrowIfBuilt();
            foreach (var name in assemblyNames)
                _ignoreConfig.IgnoredAssemblyNames.Remove(name);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        #endregion

        #region Remove Ignored Namespace

        /// <summary>
        /// Removes a previously ignored namespace, allowing types in that namespace to be scanned again.
        /// </summary>
        /// <param name="namespaceName">The namespace or namespace prefix to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredNamespace(string namespaceName)
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Remove(namespaceName);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Removes multiple previously ignored namespaces, allowing types in those namespaces to be scanned again.
        /// </summary>
        /// <param name="namespaceNames">The namespaces or namespace prefixes to stop ignoring.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder RemoveIgnoredNamespaces(params string[] namespaceNames)
        {
            ThrowIfBuilt();
            foreach (var ns in namespaceNames)
                _ignoreConfig.IgnoredNamespaces.Remove(ns);
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        #endregion

        #region Clear Ignored

        /// <summary>
        /// Clears all ignored assemblies (both by instance and by name), allowing all assemblies to be scanned.
        /// </summary>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder ClearIgnoredAssemblies()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Clear();
            _ignoreConfig.IgnoredAssemblyNames.Clear();
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Clears all ignored namespaces, allowing all namespaces to be scanned.
        /// </summary>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder ClearIgnoredNamespaces()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredNamespaces.Clear();
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        /// <summary>
        /// Clears all ignore configurations (assemblies, assembly names, and namespaces),
        /// allowing everything to be scanned.
        /// </summary>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder ClearAllIgnored()
        {
            ThrowIfBuilt();
            _ignoreConfig.IgnoredAssemblies.Clear();
            _ignoreConfig.IgnoredAssemblyNames.Clear();
            _ignoreConfig.IgnoredNamespaces.Clear();
            _ignoreConfig.InvalidateCaches();
            return this;
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets the count of registered assemblies that are currently ignored.
        /// </summary>
        /// <returns>The number of ignored assemblies.</returns>
        public virtual int GetIgnoredAssembliesCount()
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

        /// <summary>
        /// Gets the count of registered types that are currently ignored due to namespace filtering.
        /// </summary>
        /// <returns>The number of ignored types.</returns>
        public virtual int GetIgnoredTypesCount()
        {
            int count = 0;

            // Track all counted types to avoid double-counting
            var countedTypes = new HashSet<Type>();

            // 1. Explicitly registered types
            var uniqueTypes = new HashSet<Type>(_toolTypes);
            uniqueTypes.UnionWith(_promptTypes);
            uniqueTypes.UnionWith(_resourceTypes);

            foreach (var type in uniqueTypes)
            {
                if (_ignoreConfig.IsIgnored(type) && countedTypes.Add(type))
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
                        if (_ignoreConfig.IsIgnored(type) && countedTypes.Add(type))
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
