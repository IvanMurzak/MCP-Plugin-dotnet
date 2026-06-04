/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

// Reflector-module test fixtures. Each behavioral scenario lives in its OWN namespace so a test can
// isolate the module(s) it wants by registering the test assembly for module scan and then calling
// IgnoreNamespaces(...) on every sibling fixture namespace it does NOT want discovered. (Phase-α
// discovery honors the core namespace prune, mirroring ProcessAllAssemblies' type filter.)

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using com.IvanMurzak.ReflectorNet.Converter;

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared
{
    /// <summary>Marker payload type a fixture module registers a JSON converter for.</summary>
    public sealed class ModulePayload
    {
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Type a fixture module registers a reflection converter for.</summary>
    public sealed class ModuleReflectedType
    {
        public int Number { get; set; }
    }

    /// <summary>Type a fixture module blacklists from serialization.</summary>
    public sealed class ModuleBlacklistedType
    {
        public string Secret { get; set; } = string.Empty;
    }

    /// <summary>A System.Text.Json converter contributed by a fixture module.</summary>
    public sealed class ModulePayloadJsonConverter : JsonConverter<ModulePayload>
    {
        public override ModulePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new ModulePayload { Value = reader.GetString() ?? string.Empty };

        public override void Write(Utf8JsonWriter writer, ModulePayload value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Value);
    }

    /// <summary>
    /// A reflection converter contributed by a fixture module. Extends ReflectorNet's concrete
    /// generic converter so it satisfies <see cref="IReflectionConverter"/> with a parameterless ctor.
    /// </summary>
    public sealed class ModuleReflectionConverter : GenericReflectionConverter<ModuleReflectedType>
    {
    }

    /// <summary>Shared, lock-guarded sink that records the order discovered modules ran in.</summary>
    public static class OrderSink
    {
        private static readonly object _lock = new();
        private static List<string>? _active;

        public static IDisposable Begin()
        {
            lock (_lock)
            {
                _active = new List<string>();
                return new Scope();
            }
        }

        public static void Record(string id)
        {
            lock (_lock)
            {
                _active?.Add(id);
            }
        }

        public static IReadOnlyList<string> Snapshot()
        {
            lock (_lock)
            {
                return _active != null ? new List<string>(_active) : new List<string>();
            }
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose()
            {
                lock (_lock)
                {
                    _active = null;
                }
            }
        }
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution
{
    using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared;

    /// <summary>
    /// The flagship fixture: a single discoverable module that contributes a JSON converter, a
    /// reflection converter, a serialization-blacklist type, AND scan-ignore entries (assembly +
    /// namespace). Used to assert each contribution reaches effect.
    /// </summary>
    public sealed class FullContributionModule : IReflectorModule
    {
        // A non-existent assembly prefix — safe to contribute (cannot collide with any protected
        // assembly). Asserting assembly-prune *effect* end-to-end would require a second on-disk
        // assembly; the namespace-prune below gives an observable behavioral effect instead, and the
        // SafetyRail fixture covers the rejection path of both surfaces.
        public const string IgnoredAssemblyPrefix = "Some.Nonexistent.Extension.Assembly";

        // A REAL namespace hosting a dummy tool (PrunedByModuleToolClass). The module ignores it, so
        // the dummy tool must NOT be registered after build — an observable scan-ignore effect.
        public const string PrunedNamespace = "com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution.Pruned";

        public int Order => 10;

        public void Configure(IReflectorModuleContext ctx)
        {
            ctx.Reflector.JsonSerializer.AddConverter(new ModulePayloadJsonConverter());
            ctx.Reflector.Converters.Add(new ModuleReflectionConverter());
            ctx.Reflector.Converters.BlacklistType(typeof(ModuleBlacklistedType));
            ctx.Scan
                .IgnoreAssemblies(IgnoredAssemblyPrefix)
                .IgnoreNamespaces(PrunedNamespace);
        }
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution.Pruned
{
    /// <summary>
    /// A tool whose namespace the FullContributionModule contributes to the scan-ignore set. After a
    /// build that discovers that module, this tool must NOT be registered — the observable proof that
    /// a module-contributed namespace scan-ignore reached effect.
    /// </summary>
    [AiToolType]
    internal class PrunedByModuleToolClass
    {
        [AiTool("pruned-by-module-tool", "Should be pruned by a module's scan-ignore")]
        public static string Tool() => "pruned";
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution
{
    /// <summary>
    /// A control tool that lives directly under the FullContribution namespace (NOT the Pruned
    /// sub-namespace the module ignores). It must remain registered — proving the module-contributed
    /// namespace prune is scoped, not a blanket prune.
    /// </summary>
    [AiToolType]
    internal class FullContributionControlToolClass
    {
        [AiTool("full-contribution-control-tool", "Control tool that should survive")]
        public static string Tool() => "control";
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Throwing
{
    using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared;

    /// <summary>A module that throws during Configure — used to assert failure isolation.</summary>
    public sealed class ThrowingModule : IReflectorModule
    {
        public int Order => 0;

        public void Configure(IReflectorModuleContext ctx)
            => throw new InvalidOperationException("Intentional failure from ThrowingModule.");
    }

    /// <summary>A healthy module sitting alongside the throwing one — must still run.</summary>
    public sealed class SurvivingModule : IReflectorModule
    {
        public const string Id = nameof(SurvivingModule);
        public int Order => 1;

        public void Configure(IReflectorModuleContext ctx)
            => OrderSink.Record(Id);
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Ordering
{
    using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared;

    // Two ordering fixtures with intentionally inverted (Order, name) so deterministic sort is observable.
    public sealed class OrderModuleHigh : IReflectorModule
    {
        public const string Id = nameof(OrderModuleHigh);
        public int Order => 20;

        public void Configure(IReflectorModuleContext ctx) => OrderSink.Record(Id);
    }

    public sealed class OrderModuleLow : IReflectorModule
    {
        public const string Id = nameof(OrderModuleLow);
        public int Order => 5;

        public void Configure(IReflectorModuleContext ctx) => OrderSink.Record(Id);
    }
}

namespace com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.SafetyRail
{
    using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared;

    /// <summary>
    /// A module that attempts to ignore the very assembly + namespace that host it (and other
    /// modules/tools). The safety rail must reject both contributions with a warning.
    /// </summary>
    public sealed class SelfPruningModule : IReflectorModule
    {
        public int Order => 0;

        public void Configure(IReflectorModuleContext ctx)
        {
            // Both of these would prune a module/tool-hosting assembly → must be rejected by the rail.
            var ownAssemblyName = ctx.OwningAssembly.GetName().Name ?? string.Empty;
            ctx.Scan.IgnoreAssemblies(ownAssemblyName);
            ctx.Scan.IgnoreNamespaces("com.IvanMurzak.McpPlugin.Tests");
        }
    }
}
