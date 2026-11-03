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
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using com.IvanMurzak.McpPlugin.Common.Model;
using com.IvanMurzak.McpPlugin.Tests.Infrastructure;
using com.IvanMurzak.ReflectorNet;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Version = com.IvanMurzak.McpPlugin.Common.Version;

namespace com.IvanMurzak.McpPlugin.Tests.Mcp
{
    [Collection("McpPlugin")]
    public class AiToolAnnotationsValidatorTests
    {
        [AiToolType]
        public static class CompliantTools
        {
            [AiTool("validator-read", Title = "Read thing", ReadOnlyHint = true)]
            public static string Read() => "r";

            [AiTool("validator-delete", "Delete thing", DestructiveHint = true)]
            public static string Delete() => "d";

            [AiTool("validator-write", Title = "Write thing", ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false)]
            public static string Write() => "w";

            // System tools are never listed to MCP clients, so they are not checked.
            [AiTool("validator-system", ToolType = McpToolType.System)]
            public static string System() => "s";
        }

        [AiToolType]
        public static class NonCompliantTools
        {
            [AiTool("validator-no-title", ReadOnlyHint = true)]
            public static string NoTitle() => "a";

            [AiTool("validator-blank-title", "   ", DestructiveHint = false)]
            public static string BlankTitle() => "b";

            // Idempotent/open-world alone do not tell a client whether the tool is safe.
            [AiTool("validator-no-safety-hint", Title = "No safety hint", IdempotentHint = true, OpenWorldHint = false)]
            public static string NoSafetyHint() => "c";

            [AiTool("validator-contradictory", Title = "Contradictory", ReadOnlyHint = true, DestructiveHint = true)]
            public static string Contradictory() => "d";

            [AiTool("validator-nothing")]
            public static string Nothing() => "e";
        }

        // Not an [AiToolType] class: assembly scans skip it, as the builder does.
        public static class NotAToolType
        {
            [AiTool("validator-not-scanned")]
            public static string NotScanned() => "x";
        }

        readonly ITestOutputHelper _output;

        public AiToolAnnotationsValidatorTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void ValidateTypes_CompliantTools_ReturnsNoViolations()
        {
            AiToolAnnotationsValidator.ValidateTypes(new[] { typeof(CompliantTools) }).ShouldBeEmpty();
        }

        [Fact]
        public void ValidateTypes_NonCompliantTools_ReportsEachRule()
        {
            var violations = AiToolAnnotationsValidator.ValidateTypes(new[] { typeof(NonCompliantTools) });

            violations.Select(v => (v.ToolName, v.Kind)).OrderBy(x => x.ToolName).ThenBy(x => x.Kind).ShouldBe(new[]
            {
                ("validator-blank-title", AiToolAnnotationViolationKind.MissingTitle),
                ("validator-contradictory", AiToolAnnotationViolationKind.ContradictoryHints),
                ("validator-no-safety-hint", AiToolAnnotationViolationKind.MissingSafetyHint),
                ("validator-no-title", AiToolAnnotationViolationKind.MissingTitle),
                ("validator-nothing", AiToolAnnotationViolationKind.MissingTitle),
                ("validator-nothing", AiToolAnnotationViolationKind.MissingSafetyHint),
            });
        }

        [Fact]
        public void Violation_NamesToolAndDeclaringMethod()
        {
            var violation = AiToolAnnotationsValidator.ValidateTypes(new[] { typeof(NonCompliantTools) })
                .Single(v => v.ToolName == "validator-no-title");

            violation.Method.ShouldNotBeNull();
            violation.Method!.Name.ShouldBe(nameof(NonCompliantTools.NoTitle));
            violation.Message.ShouldContain("validator-no-title");
            violation.Message.ShouldContain($"{typeof(NonCompliantTools).FullName}.{nameof(NonCompliantTools.NoTitle)}");
            violation.ToString().ShouldBe(violation.Message);
        }

        [Fact]
        public void Validate_Assembly_ScansOnlyToolTypeClasses()
        {
            var violations = AiToolAnnotationsValidator.Validate(typeof(AiToolAnnotationsValidatorTests).Assembly);

            var names = violations.Select(v => v.ToolName).ToHashSet();
            names.ShouldContain("validator-nothing");
            names.ShouldNotContain("validator-not-scanned");
            names.ShouldNotContain("validator-read");
            names.ShouldNotContain("validator-system");
        }

        [Fact]
        public void ThrowIfAny_ListsEveryViolation_AndIsSilentWhenEmpty()
        {
            Should.NotThrow(() => AiToolAnnotationsValidator.ThrowIfAny(Array.Empty<AiToolAnnotationViolation>()));

            var violations = AiToolAnnotationsValidator.ValidateTypes(new[] { typeof(NonCompliantTools) });
            var ex = Should.Throw<InvalidOperationException>(() => AiToolAnnotationsValidator.ThrowIfAny(violations));
            ex.Message.ShouldStartWith($"{violations.Count} tool annotation violation(s):");
            foreach (var violation in violations)
                ex.Message.ShouldContain(violation.Message);
        }

        [Fact]
        public void ThrowIfInvalid_Assembly_ThrowsForThisTestAssembly()
        {
            // This test assembly deliberately contains non-compliant fixtures.
            Should.Throw<InvalidOperationException>(() => AiToolAnnotationsValidator.ThrowIfInvalid(typeof(AiToolAnnotationsValidatorTests).Assembly));
        }

        [Fact]
        public void NullArguments_Throw()
        {
            Should.Throw<ArgumentNullException>(() => AiToolAnnotationsValidator.Validate((Assembly[])null!));
            Should.Throw<ArgumentNullException>(() => AiToolAnnotationsValidator.ValidateTypes(null!));
            Should.Throw<ArgumentNullException>(() => AiToolAnnotationsValidator.ValidateTools(null!));
            Should.Throw<ArgumentNullException>(() => AiToolAnnotationsValidator.ThrowIfAny(null!));
        }

        [Fact]
        public async Task ValidateTools_ListedCatalogue_MatchesAttributeCheck()
        {
            var tools = await ListTools(typeof(CompliantTools), typeof(NonCompliantTools));

            tools.Select(t => t.Name).ShouldNotContain("validator-system");

            var fromList = AiToolAnnotationsValidator.ValidateTools(tools)
                .Select(v => (v.ToolName, v.Kind)).OrderBy(x => x.ToolName).ThenBy(x => x.Kind).ToList();
            var fromAttributes = AiToolAnnotationsValidator.ValidateTypes(new[] { typeof(CompliantTools), typeof(NonCompliantTools) })
                .Select(v => (v.ToolName, v.Kind)).OrderBy(x => x.ToolName).ThenBy(x => x.Kind).ToList();

            fromList.ShouldBe(fromAttributes);
            AiToolAnnotationsValidator.ValidateTools(tools).ShouldAllBe(v => v.Method == null);
        }

        async Task<ResponseListTool[]> ListTools(params Type[] types)
        {
            var mcpPlugin = new McpPluginBuilder(new Version(), new XunitTestOutputLoggerProvider(_output))
                .AddLogging(b => b.AddXunitTestOutput(_output))
                .WithTools(types)
                .Build(new Reflector());

            var response = await mcpPlugin.McpManager.ToolManager!.RunListTool(new RequestListTool());
            response.Status.ShouldBe(ResponseStatus.Success);
            return response.Value!;
        }
    }
}
