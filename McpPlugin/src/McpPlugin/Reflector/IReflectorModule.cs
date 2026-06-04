/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Reflection;
using com.IvanMurzak.ReflectorNet;
using Microsoft.Extensions.Logging;

namespace com.IvanMurzak.McpPlugin
{
    /// <summary>
    /// A unit of ReflectorNet contribution that an assembly (the core plugin OR an externally
    /// added extension) can supply. Modules are discovered automatically by assembly scan during
    /// <see cref="McpPluginBuilder.Build"/> — there is no hardcoded extension list. A discovered
    /// module may register JSON converters, reflection converters, serialization blacklist entries,
    /// and additional attribute-scan ignore rules via the supplied <see cref="IReflectorModuleContext"/>.
    /// </summary>
    /// <remarks>
    /// Discovery requirements: a module type must be a concrete (non-abstract, non-generic) class
    /// that implements <see cref="IReflectorModule"/> and exposes a public parameterless constructor
    /// (instantiated via <see cref="System.Activator.CreateInstance(System.Type)"/>). Modules placed in
    /// an assembly the core ignore-config prunes (e.g. via <c>IgnoreAssembly</c>) are never discovered,
    /// because such assemblies are not type-enumerated.
    /// </remarks>
    public interface IReflectorModule
    {
        /// <summary>
        /// Relative ordering lever applied when more than one module is discovered. Modules run in
        /// ascending <see cref="Order"/>, then by owning-assembly full name (deterministic tie-break).
        /// The core baseline module uses <c>0</c>; extensions typically use a value greater than <c>0</c>
        /// so they run after the core and may override an overlapping core JSON converter.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Contributes this module's converters and prune rules through the supplied context.
        /// Invoked once during <see cref="McpPluginBuilder.Build"/>, strictly before the heavy
        /// attribute scan. A thrown exception is caught and logged by the host; it does not abort the
        /// build nor prevent other modules (or tools) from being registered.
        /// </summary>
        /// <param name="ctx">The contribution surface (reflector, scan-ignore facade, owning assembly, logger).</param>
        void Configure(IReflectorModuleContext ctx);
    }

    /// <summary>
    /// The contribution surface handed to <see cref="IReflectorModule.Configure"/>. Exposes the
    /// <see cref="ReflectorNet.Reflector"/> (for JSON / reflection converters and serialization
    /// blacklisting) and a narrow <see cref="IScanIgnoreBuilder"/> facade (for attribute-scan prunes).
    /// </summary>
    public interface IReflectorModuleContext
    {
        /// <summary>
        /// The reflector being built. Use <c>Reflector.JsonSerializer.AddConverter(...)</c> for JSON
        /// converters, <c>Reflector.Converters.Add(...)</c> for reflection converters, and
        /// <c>Reflector.Converters.BlacklistType*(...)</c> for serialization blacklist entries.
        /// </summary>
        Reflector Reflector { get; }

        /// <summary>
        /// Narrow facade onto the host's attribute-scan ignore configuration. Lets a module prune
        /// whole assemblies / namespaces from the heavy attribute scan that runs after all modules
        /// have contributed.
        /// </summary>
        IScanIgnoreBuilder Scan { get; }

        /// <summary>
        /// The assembly the discovered module type lives in. Useful for self-relative ignore rules
        /// and diagnostics.
        /// </summary>
        Assembly OwningAssembly { get; }

        /// <summary>
        /// Logger scoped to module bootstrap. Backed by the host's logger provider (or a no-op when
        /// none was supplied to the builder).
        /// </summary>
        ILogger Logger { get; }
    }

    /// <summary>
    /// Narrow facade a module uses to contribute attribute-scan prune rules. Mirrors the host
    /// builder's <c>IgnoreAssemblies</c> / <c>IgnoreNamespaces</c> by-prefix surface, without
    /// exposing the rest of the builder. A contributed entry that would prune an assembly hosting a
    /// discovered module/tool is rejected with a logged warning (safety rail) — see
    /// <see cref="McpPluginBuilder"/> bootstrap.
    /// </summary>
    public interface IScanIgnoreBuilder
    {
        /// <summary>
        /// Excludes assemblies whose name starts with any of the supplied prefixes from the
        /// attribute scan. Returns this builder for chaining.
        /// </summary>
        IScanIgnoreBuilder IgnoreAssemblies(params string[] assemblyNamePrefixes);

        /// <summary>
        /// Excludes types whose namespace starts with any of the supplied prefixes from the
        /// attribute scan. Returns this builder for chaining.
        /// </summary>
        IScanIgnoreBuilder IgnoreNamespaces(params string[] namespacePrefixes);
    }
}
