/*
┌────────────────────────────────────────────────────────────────────────┐
│  Author: Ivan Murzak (https://github.com/IvanMurzak)                   │
│  Repository: GitHub (https://github.com/IvanMurzak/MCP-Plugin-dotnet)  │
│  Copyright (c) 2025 Ivan Murzak                                        │
│  Licensed under the Apache License, Version 2.0.                       │
│  See the LICENSE file in the project root for more information.        │
└────────────────────────────────────────────────────────────────────────┘
*/

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution;
using com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Shared;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class ReflectorModuleDiscoveryTests
    {
        private readonly XunitTestOutputLoggerProvider _loggerProvider;
        private readonly Version _version = new Version();
        private static readonly Assembly TestAssembly = typeof(FullContributionModule).Assembly;

        // Every fixture-module namespace under Data/ReflectorModules. A test keeps only the namespaces
        // it wants by ignoring the rest (phase-α discovery honors the core namespace prune).
        private const string NsFullContribution = "com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.FullContribution";
        private const string NsThrowing = "com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Throwing";
        private const string NsOrdering = "com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.Ordering";
        private const string NsSafetyRail = "com.IvanMurzak.McpPlugin.Tests.Data.ReflectorModules.SafetyRail";

        private static readonly string[] AllModuleNamespaces =
        {
            NsFullContribution, NsThrowing, NsOrdering, NsSafetyRail
        };

        public ReflectorModuleDiscoveryTests(ITestOutputHelper output)
        {
            _loggerProvider = new XunitTestOutputLoggerProvider(output);
        }

        /// <summary>Returns every fixture-module namespace EXCEPT the ones to keep.</summary>
        private static string[] NamespacesToIgnoreExcept(params string[] keep)
            => AllModuleNamespaces.Where(ns => !keep.Contains(ns)).ToArray();

        // ── Discovery: a module in a non-ignored assembly is picked up ──────────────────

        [Fact]
        public void Discovery_PicksUpModule_InNonIgnoredAssembly()
        {
            // Arrange
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreNamespaces(NamespacesToIgnoreExcept(NsFullContribution));

            // Act
            builder.Build(reflector);

            // Assert — the FullContributionModule ran, proven by its JSON converter being registered.
            reflector.JsonSerializer.GetJsonConverter(typeof(ModulePayload)).ShouldNotBeNull();
        }

        // ── Discovery is pruned when the hosting assembly is core-ignored ───────────────

        [Fact]
        public void Discovery_SkipsModule_WhenHostingAssemblyIsIgnored()
        {
            // Arrange — register the assembly for module scan, then ignore that very assembly.
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreAssembly(TestAssembly);

            // Act
            builder.Build(reflector);

            // Assert — module never ran: no converter, type not blacklisted.
            reflector.JsonSerializer.GetJsonConverter(typeof(ModulePayload)).ShouldBeNull();
            reflector.Converters.IsTypeBlacklisted(typeof(ModuleBlacklistedType)).ShouldBeFalse();
        }

        // ── Failure isolation: a throwing module is caught; others + tools survive ──────

        [Fact]
        public async Task FailureIsolation_ThrowingModuleCaught_OthersAndToolsSurvive()
        {
            // Arrange — keep only the Throwing namespace (ThrowingModule + SurvivingModule), plus tools.
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(TestAssembly)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreNamespaces(NamespacesToIgnoreExcept(NsThrowing));

            using (OrderSink.Begin())
            {
                // Act — build must NOT throw despite ThrowingModule.
                var plugin = builder.Build(reflector);

                // Assert — the healthy sibling module still ran...
                OrderSink.Snapshot().ShouldContain(Data.ReflectorModules.Throwing.SurvivingModule.Id);

                // ...and tool registration (which runs after the module phase) is unaffected.
                var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());
                response.Value.ShouldNotBeNull();
                response.Value!.ShouldContain(t => t.Name == "include-test-tool");
            }
        }

        // ── Deterministic ordering: Order then assembly name ────────────────────────────

        [Fact]
        public void Ordering_IsDeterministic_ByOrderThenAssemblyName()
        {
            // Arrange — keep only the Ordering namespace (OrderModuleLow Order=5, OrderModuleHigh Order=20).
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreNamespaces(NamespacesToIgnoreExcept(NsOrdering));

            using (OrderSink.Begin())
            {
                // Act
                builder.Build(reflector);

                // Assert — ascending Order: Low (5) before High (20).
                var order = OrderSink.Snapshot();
                order.ShouldBe(new[]
                {
                    Data.ReflectorModules.Ordering.OrderModuleLow.Id,
                    Data.ReflectorModules.Ordering.OrderModuleHigh.Id
                });
            }
        }

        // ── Full contribution: JSON + reflection converter + blacklist + scan-ignore ────

        [Fact]
        public async Task FullContribution_EachContributionReachesEffect()
        {
            // Arrange — keep only the FullContribution namespace; register tools from the same assembly
            // so the module-contributed namespace scan-ignore has an observable target.
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(TestAssembly)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreNamespaces(NamespacesToIgnoreExcept(NsFullContribution));

            // Act
            var plugin = builder.Build(reflector);

            // Assert — (1) JSON converter registered.
            reflector.JsonSerializer.GetJsonConverter(typeof(ModulePayload)).ShouldNotBeNull();

            // (2) Reflection converter registered (resolvable for the target type).
            reflector.Converters.GetConverter(typeof(ModuleReflectedType)).ShouldNotBeNull();
            reflector.Converters.GetAllSerializers()
                .Any(c => c is ModuleReflectionConverter).ShouldBeTrue();

            // (3) Serialization blacklist applied.
            reflector.Converters.IsTypeBlacklisted(typeof(ModuleBlacklistedType)).ShouldBeTrue();

            // (4) Scan-ignore (namespace) reached effect: the tool in the module-ignored sub-namespace
            // is pruned, while the control tool in the parent namespace survives.
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());
            response.Value.ShouldNotBeNull();
            response.Value!.ShouldNotContain(t => t.Name == "pruned-by-module-tool");
            response.Value!.ShouldContain(t => t.Name == "full-contribution-control-tool");
        }

        // ── Safety rail: a contributed scan-ignore must not prune a module/tool-hosting assembly ──

        [Fact]
        public async Task SafetyRail_RejectsSelfPrune_OfModuleAndToolHostingAssembly()
        {
            // Arrange — keep only the SafetyRail namespace; register tools from the same assembly.
            // SelfPruningModule attempts to ignore the test assembly's name AND the test root namespace.
            var reflector = new Reflector();
            var builder = new McpPluginBuilder(_version, _loggerProvider)
                .WithToolsFromAssembly(TestAssembly)
                .WithReflectorModulesFromAssembly(new[] { TestAssembly })
                .IgnoreNamespaces(NamespacesToIgnoreExcept(NsSafetyRail));

            // Act — both self-prune attempts must be rejected by the rail.
            var plugin = builder.Build(reflector);

            // Assert — the test tool (namespace under com.IvanMurzak.McpPlugin.Tests) is STILL registered,
            // proving the namespace self-prune was rejected. If the rail had let it through, the tool's
            // type would have been hidden from the attribute scan.
            var response = await plugin.McpManager.ToolManager!.RunListTool(new RequestListTool());
            response.Value.ShouldNotBeNull();
            response.Value!.ShouldContain(t => t.Name == "include-test-tool");
        }
    }
}
