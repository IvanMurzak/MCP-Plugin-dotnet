/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
└────────────────────────────────────────────────────────────────────────┘
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.IvanMurzak.ReflectorNet;
using com.IvanMurzak.ReflectorNet.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace com.IvanMurzak.McpPlugin
{
    public partial class McpPluginBuilder
    {
        // Assemblies the builder will scan for IReflectorModule implementors.
        protected readonly List<Assembly> _reflectorModuleAssemblies = new();

        /// <summary>
        /// Registers one or more assemblies to be scanned for <see cref="IReflectorModule"/>
        /// implementors during <see cref="Build"/>. Discovery is cheap (top-level type +
        /// <c>IsAssignableFrom</c>, no method/schema work) and reuses the existing core ignore-config
        /// prune, so heavy assemblies pruned by name are never type-enumerated.
        /// </summary>
        /// <param name="assemblies">The assemblies to scan. Null entries are skipped.</param>
        /// <returns>The current builder instance for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="assemblies"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the builder has already been built.</exception>
        public virtual IMcpPluginBuilder WithReflectorModulesFromAssembly(IEnumerable<Assembly> assemblies)
        {
            ThrowIfBuilt();

            if (assemblies == null)
                throw new ArgumentNullException(nameof(assemblies));

            foreach (var assembly in assemblies)
            {
                if (assembly == null)
                    continue;
                _reflectorModuleAssemblies.Add(assembly);
            }
            return this;
        }

        /// <summary>
        /// Runs the phased reflector-module bootstrap: (α) cheap discovery of <see cref="IReflectorModule"/>
        /// implementors in non-ignored registered assemblies; (β) ordered, failure-isolated invocation of
        /// each module's <see cref="IReflectorModule.Configure"/>; the host's heavy attribute scan (γ) runs
        /// afterwards in <see cref="Build"/>. Must be called strictly BEFORE <c>ProcessAllAssemblies()</c>.
        /// </summary>
        protected virtual void BootstrapReflectorModules(Reflector reflector)
        {
            // ── Phase α: cheap discovery ───────────────────────────────────────────────
            // Enumerate types ONLY in registered assemblies that survive the static core
            // ignore-config prune. Heavy assemblies pruned by name are never type-enumerated.
            var modules = new List<IReflectorModule>();
            var moduleAssemblies = new HashSet<Assembly>();
            // Namespaces that host a discovered module. The namespace safety rail protects these so a
            // contributed namespace prune cannot hide a module's own namespace (which would defeat the
            // discovery the host just performed). A module may still prune OTHER namespaces (e.g. a
            // sub-namespace of unrelated tools) — that is the feature's whole point.
            var moduleNamespaces = new HashSet<string>(StringComparer.Ordinal);

            foreach (var assembly in _reflectorModuleAssemblies.Distinct())
            {
                if (_ignoreConfig.IsIgnored(assembly))
                    continue;

                foreach (var type in AssemblyUtils.GetAssemblyTypes(assembly))
                {
                    // Honor the core namespace prune too, mirroring ProcessAllAssemblies' type filter:
                    // a module in a core-ignored namespace is not discovered.
                    if (_ignoreConfig.IsIgnored(type))
                        continue;

                    // Top-level shape gate only — no GetMethods / schema work.
                    if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                        continue;
                    if (!typeof(IReflectorModule).IsAssignableFrom(type))
                        continue;

                    IReflectorModule? module;
                    try
                    {
                        module = (IReflectorModule?)Activator.CreateInstance(type);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to instantiate reflector module '{Module}'. Skipping it. A public parameterless constructor is required.", type.FullName);
                        continue;
                    }

                    if (module == null)
                        continue;

                    modules.Add(module);
                    moduleAssemblies.Add(assembly);
                    if (!string.IsNullOrEmpty(type.Namespace))
                        moduleNamespaces.Add(type.Namespace!);
                }
            }

            if (modules.Count == 0)
                return;

            // The protected set: assemblies that host a discovered module OR are registered as a
            // tool/prompt/resource/skill assembly. A contributed scan-ignore entry must never prune
            // any of these (safety rail, enforced inside ReflectorModuleContext.Scan).
            var protectedAssemblies = new HashSet<Assembly>(moduleAssemblies);
            protectedAssemblies.UnionWith(_toolAssemblies);
            protectedAssemblies.UnionWith(_promptAssemblies);
            protectedAssemblies.UnionWith(_resourceAssemblies);
            protectedAssemblies.UnionWith(_skillAssemblies);

            // ── Phase β: ordered, failure-isolated contribution ────────────────────────
            // Sort by Order, then by owning-assembly full name for a deterministic tie-break.
            var ordered = modules
                .Select(m => new { Module = m, Assembly = m.GetType().Assembly })
                .OrderBy(x => x.Module.Order)
                .ThenBy(x => x.Assembly.FullName, StringComparer.Ordinal)
                .ToList();

            foreach (var entry in ordered)
            {
                var moduleType = entry.Module.GetType();
                var moduleLogger = _loggerProvider?.CreateLogger(moduleType.FullName ?? nameof(IReflectorModule))
                    ?? (ILogger)NullLogger.Instance;
                var ctx = new ReflectorModuleContext(reflector, entry.Assembly, moduleLogger, _ignoreConfig, protectedAssemblies, moduleNamespaces, _logger);

                try
                {
                    entry.Module.Configure(ctx);
                }
                catch (Exception ex)
                {
                    // Failure isolation: one throwing module must not abort the build or stop the rest.
                    _logger?.LogError(ex, "Reflector module '{Module}' threw during Configure and was skipped. Other modules and tools are unaffected.", moduleType.FullName);
                }
            }
            // ── Phase γ runs next in Build(): ProcessAllAssemblies() under the augmented config. ──
        }

        /// <summary>
        /// Default <see cref="IReflectorModuleContext"/> implementation. Wraps the host reflector and
        /// exposes a guarded <see cref="IScanIgnoreBuilder"/> that refuses to prune any assembly hosting
        /// a discovered module or registered tool/prompt/resource/skill.
        /// </summary>
        protected sealed class ReflectorModuleContext : IReflectorModuleContext, IScanIgnoreBuilder
        {
            private readonly McpPluginBuilderIgnoreConfig _ignore;
            private readonly IReadOnlyCollection<Assembly> _protectedAssemblies;
            private readonly IReadOnlyCollection<string> _moduleNamespaces;
            private readonly ILogger? _hostLogger;

            public Reflector Reflector { get; }
            public Assembly OwningAssembly { get; }
            public ILogger Logger { get; }
            public IScanIgnoreBuilder Scan => this;

            public ReflectorModuleContext(
                Reflector reflector,
                Assembly owningAssembly,
                ILogger logger,
                McpPluginBuilderIgnoreConfig ignore,
                IReadOnlyCollection<Assembly> protectedAssemblies,
                IReadOnlyCollection<string> moduleNamespaces,
                ILogger? hostLogger)
            {
                Reflector = reflector ?? throw new ArgumentNullException(nameof(reflector));
                OwningAssembly = owningAssembly ?? throw new ArgumentNullException(nameof(owningAssembly));
                Logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _ignore = ignore ?? throw new ArgumentNullException(nameof(ignore));
                _protectedAssemblies = protectedAssemblies ?? throw new ArgumentNullException(nameof(protectedAssemblies));
                _moduleNamespaces = moduleNamespaces ?? throw new ArgumentNullException(nameof(moduleNamespaces));
                _hostLogger = hostLogger;
            }

            public IScanIgnoreBuilder IgnoreAssemblies(params string[] assemblyNamePrefixes)
            {
                if (assemblyNamePrefixes == null)
                    return this;

                foreach (var prefix in assemblyNamePrefixes)
                {
                    if (string.IsNullOrEmpty(prefix))
                        continue;

                    // Safety rail: reject a prefix that would prune a module/tool-hosting assembly.
                    var collision = _protectedAssemblies.FirstOrDefault(a =>
                    {
                        var name = a.GetName().Name;
                        return !string.IsNullOrEmpty(name) && name!.StartsWith(prefix, StringComparison.Ordinal);
                    });
                    if (collision != null)
                    {
                        _hostLogger?.LogWarning(
                            "Reflector module from '{Owner}' tried to ignore assembly prefix '{Prefix}', which would prune '{Protected}' that hosts a discovered module or registered tools. Ignoring this entry.",
                            OwningAssembly.GetName().Name, prefix, collision.GetName().Name);
                        continue;
                    }

                    _ignore.IgnoredAssemblyNames.Add(prefix);
                }
                _ignore.InvalidateCaches();
                return this;
            }

            public IScanIgnoreBuilder IgnoreNamespaces(params string[] namespacePrefixes)
            {
                if (namespacePrefixes == null)
                    return this;

                foreach (var prefix in namespacePrefixes)
                {
                    if (string.IsNullOrEmpty(prefix))
                        continue;

                    // Safety rail: reject a namespace prefix that would hide a DISCOVERED MODULE's own
                    // namespace (which would defeat the discovery just performed). A module may still
                    // prune any other namespace — pruning unrelated tool namespaces is the feature's
                    // purpose, so we deliberately do NOT block those.
                    var collision = _moduleNamespaces
                        .FirstOrDefault(ns => ns.StartsWith(prefix, StringComparison.Ordinal));
                    if (collision != null)
                    {
                        _hostLogger?.LogWarning(
                            "Reflector module from '{Owner}' tried to ignore namespace prefix '{Prefix}', which would prune discovered-module namespace '{Protected}'. Ignoring this entry.",
                            OwningAssembly.GetName().Name, prefix, collision);
                        continue;
                    }

                    _ignore.IgnoredNamespaces.Add(prefix);
                }
                _ignore.InvalidateCaches();
                return this;
            }
        }
    }
}
